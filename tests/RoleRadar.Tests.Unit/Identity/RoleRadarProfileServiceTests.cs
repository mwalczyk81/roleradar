using System.Security.Claims;
using Duende.IdentityServer.Extensions;
using Duende.IdentityServer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

public sealed class RoleRadarProfileServiceTests
{
    [Fact]
    public async Task RoleRadarUserClaimsPrincipalFactory_EmitsPreferredUsernameClaim()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole());
        services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentityAppDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<RoleRadarUserClaimsPrincipalFactory>();

        services.AddScoped(_ => dbContext);

        using var provider = services.BuildServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var factory = provider.GetRequiredService<IUserClaimsPrincipalFactory<IdentityUser<Guid>>>();

        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = "local-owner"
        };
        await userManager.CreateAsync(user, "Password123!");

        var principal = await factory.CreateAsync(user);

        var preferredUsernameClaim = principal.Claims.FirstOrDefault(c => c.Type == "preferred_username");
        Assert.NotNull(preferredUsernameClaim);
        Assert.Equal("local-owner", preferredUsernameClaim.Value);
    }

    [Fact]
    public async Task RoleRadarProfileService_EmitsPreferredUsernameClaim_WhenProfileRequested()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole());
        services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentityAppDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<RoleRadarUserClaimsPrincipalFactory>();

        services.AddScoped(_ => dbContext);
        services.AddScoped<RoleRadarProfileService>();

        using var provider = services.BuildServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        var profileService = provider.GetRequiredService<RoleRadarProfileService>();

        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = "owner-alice"
        };
        await userManager.CreateAsync(user, "Password123!");

        var identity = new ClaimsIdentity([
            new Claim("sub", user.Id.ToString("D")),
            new Claim("name", user.UserName)
        ], "Identity.Application");
        var subject = new ClaimsPrincipal(identity);

        var context = new ProfileDataRequestContext(
            subject,
            new Client { ClientId = "roleradar-web" },
            "UserInfoEndpoint",
            ["preferred_username", "name"]
        );

        await profileService.GetProfileDataAsync(context);

        var claim = context.IssuedClaims.FirstOrDefault(c => c.Type == "preferred_username");
        Assert.NotNull(claim);
        Assert.Equal("owner-alice", claim.Value);
    }
}
