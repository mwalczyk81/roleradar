using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RoleRadar.Identity.Configuration;
using RoleRadar.Identity.Data;

namespace RoleRadar.Identity;

public partial class Program
{
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

        builder.Services.AddRazorPages();

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

        builder.Services.AddIdentityServer(options =>
        {
            options.Events.RaiseErrorEvents = true;
            options.Events.RaiseInformationEvents = true;
            options.Events.RaiseFailureEvents = true;
            options.Events.RaiseSuccessEvents = true;
            options.KeyManagement.Enabled = true;
            options.KeyManagement.RotationInterval = TimeSpan.FromDays(30);
        })
        .AddAspNetIdentity<IdentityUser<Guid>>()
        .AddConfigurationStore<IdentityAppDbContext>()
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
