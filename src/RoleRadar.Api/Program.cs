using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.OData;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using OpenTelemetry.Trace;
using RoleRadar.Api.Authentication;
using RoleRadar.Api.Endpoints;
using RoleRadar.Api.Filters;
using RoleRadar.Api.Observability;
using RoleRadar.Api.OpenApi;
using RoleRadar.Api.Problems;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Reading.Models;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure;
using RoleRadar.Observability;

namespace RoleRadar.Api;

public partial class Program
{
    public const string OwnerPolicyName = "OwnerAllowlistPolicy";
    public const string ReadPolicyName = "ApiReadScopePolicy";
    public const string WritePolicyName = "ApiWriteScopePolicy";
    public const string FullAccessScope = "roleradar.api";
    public const string ReadOnlyScope = "roleradar.api.read";

    /// <summary>What the image's built-in <c>--healthcheck</c> calls unless ROLERADAR_HEALTHCHECK_URL says otherwise: readiness, not just liveness.</summary>
    public const string DefaultHealthcheckUrl = "http://127.0.0.1:8080/health/ready";

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--healthcheck", StringComparer.OrdinalIgnoreCase))
        {
            var healthUrl = Environment.GetEnvironmentVariable("ROLERADAR_HEALTHCHECK_URL")
                ?? DefaultHealthcheckUrl;
            using var handler = new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(2) };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
            try
            {
                using var response = await client.GetAsync(healthUrl);
                return response.IsSuccessStatusCode ? 0 : 1;
            }
            catch
            {
                return 1;
            }
        }

        var app = CreateApp(args);
        await app.RunAsync();
        return 0;
    }

    public static WebApplication CreateApp(
            string[]? args = null,
            Action<WebApplicationBuilder>? configureBuilder = null,
            string? entryAssemblyName = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args ?? [],
            ApplicationName = typeof(Program).Assembly.GetName().Name
        });

        // Logging first, so providers a test adds through configureBuilder are kept (feature 013, ADR 0041).
        builder.Logging.ClearProviders().AddRoleRadarLogging("api", LogStream.Split, builder.Configuration);

        // OpenTelemetry tracing (feature 015, ADR 0050, ADR 0050 addendum): registered before configureBuilder so RoleRadarSpanAttributeProcessor
        // runs ahead of any test exporter added via ConfigureOpenTelemetryTracerProvider.
        // Uses OpenTelemetry.Instrumentation.AspNetCore (Principle X, maintained library) instead of hand-set span tags.
        builder.Services.AddRoleRadarTelemetry("roleradar-api", builder.Configuration, tracing =>
        {
            tracing.AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = false;
            });
        });

        builder.Services.AddHttpLogging(logging =>
        {
            logging.LoggingFields = HttpLoggingFields.RequestMethod
                | HttpLoggingFields.RequestPath
                | HttpLoggingFields.ResponseStatusCode
                | HttpLoggingFields.Duration;
            logging.CombineLogs = true;
        });

        configureBuilder?.Invoke(builder);

        builder.Services.AddHttpContextAccessor();
        builder.Services.Configure<OwnerSecurityOptions>(builder.Configuration.GetSection(OwnerSecurityOptions.SectionName));
        builder.Services.TryAddScoped<IOwnerIdProvider, HttpUserOwnerIdProvider>();
        builder.Services.AddScoped<HttpActorAccessor>();
        builder.Services.Replace(ServiceDescriptor.Scoped<IRequestContext, HttpRequestContext>());
        builder.Services.AddSingleton<IAuthorizationHandler, OwnerAllowlistHandler>();
        builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();
        builder.Services.AddHostedService<RoleRadar.Api.OData.ReadIndexStartupService>();
        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemTypes.Customize);
        builder.Services.AddHealthChecks()
            .AddCheck<ApiReadinessCheck>("mongo", failureStatus: HealthStatus.Unhealthy, tags: [HealthEndpoints.ReadyTag]);

        var authority = builder.Configuration["Authentication:Authority"]
            ?? Environment.GetEnvironmentVariable("ROLERADAR_OIDC_AUTHORITY")
            ?? "https://localhost:5001";
        var audience = builder.Configuration["Authentication:Audience"]
            ?? Environment.GetEnvironmentVariable("ROLERADAR_OIDC_AUDIENCE")
            ?? "roleradar.api";

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = audience;
                options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(60),
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256]
                };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(OwnerPolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new OwnerAllowlistRequirement());
            })
            .AddPolicy(ReadPolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(
                    new OwnerAllowlistRequirement(),
                    new ScopeRequirement(FullAccessScope, ReadOnlyScope));
            })
            .AddPolicy(WritePolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(
                    new OwnerAllowlistRequirement(),
                    new ScopeRequirement(FullAccessScope));
            });

        builder.Services.AddRoleRadarApplication();
        var mongoConnectionString = builder.Configuration.GetConnectionString("MongoConnection")
            ?? RoleRadar.Infrastructure.Mongo.MongoConnectionStringSource.Resolve(Environment.GetEnvironmentVariable);
        var mongoDatabaseName = builder.Configuration["DatabaseName"]
            ?? Environment.GetEnvironmentVariable("ROLERADAR_MONGO_DATABASE");

        builder.Services.AddRoleRadarInfrastructure(options =>
        {
            if (!string.IsNullOrWhiteSpace(mongoConnectionString))
            {
                options.ConnectionString = mongoConnectionString.Trim();
            }

            if (!string.IsNullOrWhiteSpace(mongoDatabaseName))
            {
                options.DatabaseName = mongoDatabaseName.Trim();
            }
        });

        // Demo mode (feature 012, ADR 0039): refuse to start unless demo mode and the database agree. A no-op for any
        // ordinary configuration.
        var defaults = new RoleRadar.Infrastructure.Mongo.MongoOptions();
        DemoStartupCheck.Enforce(
            Environment.GetEnvironmentVariable(RoleRadar.Infrastructure.Mongo.DemoModeGuard.DemoModeVariable),
            string.IsNullOrWhiteSpace(mongoDatabaseName) ? defaults.DatabaseName : mongoDatabaseName.Trim(),
            string.IsNullOrWhiteSpace(mongoConnectionString) ? defaults.ConnectionString : mongoConnectionString.Trim());

        builder.Services.AddOpenApi(options =>
        {
            options.AddExplicitTypeTransformers();
            options.AddProblemDetailsMembers();
        });

        builder.Services.AddControllers(options =>
        {
            options.Filters.Add<ODataExceptionFilter>();
        })
        .AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        })
        .AddOData(options =>
        {
            options.AddRouteComponents("odata", GetEdmModel())
                .Select()
                .Filter()
                .OrderBy()
                .Count()
                .SetMaxTop(100);
        });

        // Build-time OpenAPI generation (dotnet-getdocument) runs this entry point under a mock server, so every startup service runs
        // too. One that cannot reach Mongo logs an error, and since feature 013 warn and above go to stderr, which MSBuild's Exec
        // task turns into a build error (MSB3077) on any machine with no Mongo, such as CI. Generating the document needs none of
        // them, so drop what the application and its libraries registered: the read-index service, the data-protection key ring (its
        // "no XML encryptor" warning), the health-check publisher. The web host's own service is added at Build and stays.
        if (OpenApiDocumentGeneration.IsGeneratingDocument(entryAssemblyName ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name))
        {
            builder.Services.RemoveAll<IHostedService>();
        }

        // Intentionally zero CORS middleware registered (server-to-server only per ADR-0018 / FR-009)
        var app = builder.Build();

        app.UseHttpLogging();

        // Right after the request log middleware, so an escaped exception becomes a problem document with the request's trace id.
        // Every failure is handled by ApiExceptionHandler; the no-op delegate only satisfies the middleware's own options.
        app.UseExceptionHandler(new ExceptionHandlerOptions { ExceptionHandler = static _ => Task.CompletedTask });

        // An empty 401, 403 or 404 written by authentication, authorization or routing gets the same problem document.
        app.UseStatusCodePages();

        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapRoleRadarHealth();

        // Compatibility alias of /health/live. A plain endpoint, not a health check, so the OpenAPI document keeps listing it.
        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
            .AllowAnonymous()
            .WithHttpLogging(HttpLoggingFields.None);

        var apiV1 = app.MapGroup("/api/v1")
            .RequireAuthorization(OwnerPolicyName)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        apiV1.MapPostingsEndpoints();
        apiV1.MapCompaniesEndpoints();
        apiV1.MapApplicationsEndpoints();
        apiV1.MapAncillaryEndpoints();
        apiV1.MapInsightsEndpoints();

        app.MapControllers();

        return app;
    }

    public static IEdmModel GetEdmModel()
    {
        var builder = new ODataConventionModelBuilder();
        builder.EnableLowerCamelCase();
        builder.EntitySet<PostingReadModel>("Postings");
        builder.EntitySet<CompanyReadModel>("Companies");
        builder.EntitySet<ApplicationReadModel>("Applications");
        builder.EntitySet<NoteReadModel>("Notes");
        builder.EntitySet<CriterionReadModel>("Criteria");
        builder.EntitySet<SearchRunReadModel>("SearchRuns");
        return builder.GetEdmModel();
    }

    private sealed class OwnerAllowlistRequirement : IAuthorizationRequirement;

    private sealed class OwnerAllowlistHandler(IOptions<OwnerSecurityOptions> options)
        : AuthorizationHandler<OwnerAllowlistRequirement>
    {
        private readonly OwnerSecurityOptions _options = options.Value;

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            OwnerAllowlistRequirement requirement)
        {
            var sub = context.User.FindFirst("sub")?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrWhiteSpace(sub) &&
                _options.AllowedSubjects is { Length: > 0 } allowed &&
                allowed.Any(s => !string.IsNullOrWhiteSpace(s) && string.Equals(s, sub, StringComparison.Ordinal)))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ScopeRequirement(params string[] allowedScopes) : IAuthorizationRequirement
    {
        public IReadOnlyList<string> AllowedScopes { get; } = allowedScopes;
    }

    private sealed class ScopeAuthorizationHandler : AuthorizationHandler<ScopeRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ScopeRequirement requirement)
        {
            var tokenScopes = context.User.FindAll("scope")
                .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            if (tokenScopes.Any(s => requirement.AllowedScopes.Contains(s, StringComparer.Ordinal)))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
