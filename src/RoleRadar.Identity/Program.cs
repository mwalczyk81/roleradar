using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RoleRadar.Identity.Configuration;
using RoleRadar.Identity.Data;

namespace RoleRadar.Identity;

public partial class Program
{
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

        var dpKeysDir = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "roleradar-identity-dpkeys"));
        dpKeysDir.Create();
        builder.Services.AddDataProtection()
            .PersistKeysToFileSystem(dpKeysDir);

        builder.Services.AddRazorPages()
            .AddApplicationPart(typeof(Program).Assembly);

        var identityConnectionString = builder.Configuration.GetConnectionString("IdentityConnection")
            ?? Environment.GetEnvironmentVariable("ROLERADAR_IDENTITY_DB")
            ?? "Data Source=identity.db";

        builder.Services.AddDbContext<IdentityAppDbContext>(options =>
            options.UseSqlite(identityConnectionString));

        builder.Services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(options =>
        {
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddEntityFrameworkStores<IdentityAppDbContext>()
        .AddDefaultTokenProviders();

        var issuerUri = builder.Configuration["Identity:IssuerUri"]
            ?? Environment.GetEnvironmentVariable("ROLERADAR_IDENTITY_ISSUER_URI");

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
            options.KeyManagement.KeyPath = Path.Combine(Path.GetTempPath(), "roleradar-identity-keys");
        })
        .AddAspNetIdentity<IdentityUser<Guid>>()
        .AddOperationalStore<IdentityAppDbContext>(options =>
        {
            options.EnableTokenCleanup = true;
            options.TokenCleanupInterval = 3600;
        })
        .AddInMemoryIdentityResources(IdentityServerConfig.GetIdentityResources())
        .AddInMemoryApiScopes(IdentityServerConfig.GetApiScopes())
        .AddInMemoryApiResources(IdentityServerConfig.GetApiResources())
        .AddInMemoryClients(IdentityServerConfig.GetClients(builder.Configuration));

        IdentityServerConfig.ConfigureRateLimiting(builder.Services);

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
            dbContext.Database.Migrate();
        }

        app.UseRateLimiter();
        app.UseRouting();

        app.UseIdentityServer();
        app.UseAuthorization();

        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
            .AllowAnonymous();

        app.MapRazorPages().RequireRateLimiting(IdentityServerConfig.AuthRateLimitPolicy);

        return app;
    }
}
