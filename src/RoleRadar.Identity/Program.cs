using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RoleRadar.Identity.Configuration;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Observability;
using RoleRadar.Identity.Services;
using RoleRadar.Observability;

namespace RoleRadar.Identity;

public partial class Program
{
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
        Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args ?? [],
            ApplicationName = typeof(Program).Assembly.GetName().Name
        });

        // Logging first, so providers a test adds through configureBuilder are kept (feature 013, ADR 0041).
        builder.Logging.ClearProviders().AddRoleRadarLogging("identity", LogStream.Split, builder.Configuration);

        configureBuilder?.Invoke(builder);

        var identityConnectionString = builder.Configuration.GetConnectionString("IdentityConnection")
            ?? Environment.GetEnvironmentVariable("ROLERADAR_IDENTITY_DB")
            ?? "Data Source=/data/identity/identity.db";

        var sqliteDataSource = new SqliteConnectionStringBuilder(identityConnectionString).DataSource;
        var identityDataDir = Path.GetDirectoryName(sqliteDataSource);
        if (string.IsNullOrWhiteSpace(identityDataDir))
        {
            identityDataDir = "/data/identity";
        }

        var dpKeysDir = new DirectoryInfo(Path.Combine(identityDataDir, "dpkeys"));
        dpKeysDir.Create();
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(dpKeysDir);

        var signingKeysDir = Path.Combine(identityDataDir, "keys");
        Directory.CreateDirectory(signingKeysDir);

        builder.Services.AddRazorPages()
            .AddApplicationPart(typeof(Program).Assembly);

        builder.Services.AddDbContext<IdentityAppDbContext>(options =>
            options.UseSqlite(identityConnectionString));

        builder.Services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(options =>
        {
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddEntityFrameworkStores<IdentityAppDbContext>()
        .AddDefaultTokenProviders()
        .AddClaimsPrincipalFactory<RoleRadar.Identity.Services.RoleRadarUserClaimsPrincipalFactory>();

        // Feature 014 (ADR 0042): the sign-in page checks one-time codes through this service (platform provider + replay guard).
        builder.Services.AddScoped<RoleRadar.Identity.Services.OwnerProvisioningService>();

        var issuerUri = builder.Configuration["Identity:IssuerUri"]
            ?? Environment.GetEnvironmentVariable("ROLERADAR_IDENTITY_ISSUER_URI");

        var clients = IdentityServerConfig.GetClients(builder.Configuration).ToList();

        builder.Services.AddIdentityServer(options =>
        {
            if (!string.IsNullOrWhiteSpace(issuerUri))
            {
                options.IssuerUri = issuerUri.Trim();
            }

            options.Events.RaiseErrorEvents = true;
            options.Events.RaiseInformationEvents = true;
            options.Events.RaiseFailureEvents = true;
            options.Events.RaiseSuccessEvents = true;
            options.KeyManagement.Enabled = true;
            options.KeyManagement.RotationInterval = TimeSpan.FromDays(30);
            options.KeyManagement.KeyPath = signingKeysDir;
        })
        .AddAspNetIdentity<IdentityUser<Guid>>()
        .AddProfileService<RoleRadar.Identity.Services.RoleRadarProfileService>()
        .AddOperationalStore<IdentityAppDbContext>(options =>
        {
            options.EnableTokenCleanup = true;
            options.TokenCleanupInterval = 3600;
        })
        .AddInMemoryIdentityResources(IdentityServerConfig.GetIdentityResources())
        .AddInMemoryApiScopes(IdentityServerConfig.GetApiScopes())
        .AddInMemoryApiResources(IdentityServerConfig.GetApiResources())
        .AddInMemoryClients(clients);

        IdentityServerConfig.ConfigureRateLimiting(builder.Services);

        builder.Services.AddHealthChecks()
            .AddCheck<IdentityReadinessCheck>("identity-db", failureStatus: HealthStatus.Unhealthy, tags: [HealthEndpoints.ReadyTag]);

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
            dbContext.Database.Migrate();
        }

        app.UseMiddleware<CorrelationMiddleware>();

        // 009 US4: the kit and Identity assets, then the /Account headers (form-action comes from the registered clients).
        app.UseStaticFiles();
        app.UseAccountSecurityHeaders(clients);

        app.UseRateLimiter();
        app.UseRouting();

        app.UseIdentityServer();
        app.UseAuthorization();

        app.MapRoleRadarHealth();

        app.MapRazorPages().RequireRateLimiting(IdentityServerConfig.AuthRateLimitPolicy);

        return app;
    }
}
