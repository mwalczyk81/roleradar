using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using RoleRadar.Api.Authentication;
using RoleRadar.Api.Endpoints;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure;

namespace RoleRadar.Api;

public partial class Program
{
    public const string OwnerPolicyName = "OwnerAllowlistPolicy";

    public static async Task<int> Main(string[] args)
    {
        var app = CreateApp(args);
        await app.RunAsync();
        return 0;
    }

    public static WebApplication CreateApp(
            string[]? args = null,
            Action<WebApplicationBuilder>? configureBuilder = null)
        {
            var builder = WebApplication.CreateBuilder(args ?? []);

            configureBuilder?.Invoke(builder);

            builder.Services.AddHttpContextAccessor();
            builder.Services.Configure<OwnerSecurityOptions>(builder.Configuration.GetSection(OwnerSecurityOptions.SectionName));
            builder.Services.Configure<OwnerSecurityOptions>(builder.Configuration.GetSection("Owner"));
            builder.Services.AddScoped<IOwnerIdProvider, HttpUserOwnerIdProvider>();
            builder.Services.AddSingleton<IAuthorizationHandler, OwnerAllowlistHandler>();

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

            builder.Services.AddOpenApi();

            // Intentionally zero CORS middleware registered (server-to-server only per ADR-0018 / FR-009)
            var app = builder.Build();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapOpenApi();

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
                    allowed.Contains(sub, StringComparer.Ordinal))
                {
                    context.Succeed(requirement);
                }

            return Task.CompletedTask;
        }
    }
}
