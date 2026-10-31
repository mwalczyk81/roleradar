using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RoleRadar.Api.Authentication;
using RoleRadar.Api.Endpoints;
using RoleRadar.Api.OpenApi;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure;

namespace RoleRadar.Api;

public partial class Program
{
    public const string OwnerPolicyName = "OwnerAllowlistPolicy";
    public const string ReadPolicyName = "ApiReadScopePolicy";
    public const string WritePolicyName = "ApiWriteScopePolicy";
    public const string FullAccessScope = "roleradar.api";
    public const string ReadOnlyScope = "roleradar.api.read";

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--healthcheck", StringComparer.OrdinalIgnoreCase))
        {
            var healthUrl = Environment.GetEnvironmentVariable("ROLERADAR_HEALTHCHECK_URL")
                ?? "http://127.0.0.1:8080/health";
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
            Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args ?? [],
            ApplicationName = typeof(Program).Assembly.GetName().Name
        });

        configureBuilder?.Invoke(builder);

        builder.Services.AddHttpContextAccessor();
        builder.Services.Configure<OwnerSecurityOptions>(builder.Configuration.GetSection(OwnerSecurityOptions.SectionName));
        builder.Services.AddScoped<IOwnerIdProvider, HttpUserOwnerIdProvider>();
        builder.Services.AddSingleton<IAuthorizationHandler, OwnerAllowlistHandler>();
        builder.Services.AddSingleton<IAuthorizationHandler, ScopeAuthorizationHandler>();

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
        builder.Services.AddRoleRadarInfrastructure(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("MongoConnection")
                ?? RoleRadar.Infrastructure.Mongo.MongoConnectionStringSource.Resolve(Environment.GetEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                options.ConnectionString = connectionString.Trim();
            }

            var databaseName = builder.Configuration["DatabaseName"]
                ?? Environment.GetEnvironmentVariable("ROLERADAR_MONGO_DATABASE");
            if (!string.IsNullOrWhiteSpace(databaseName))
            {
                options.DatabaseName = databaseName.Trim();
            }
        });

        builder.Services.AddOpenApi(options =>
        {
            options.AddExplicitTypeTransformers();
        });

        // Intentionally zero CORS middleware registered (server-to-server only per ADR-0018 / FR-009)
        var app = builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
            .AllowAnonymous();

        var apiV1 = app.MapGroup("/api/v1")
            .RequireAuthorization(OwnerPolicyName);

        apiV1.MapPostingsEndpoints();
        apiV1.MapCompaniesEndpoints();
        apiV1.MapApplicationsEndpoints();
        apiV1.MapAncillaryEndpoints();

        return app;
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
