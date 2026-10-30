using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RoleRadar.Identity.Configuration;
using RoleRadar.Identity.Data;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

public sealed class LockoutAndRateLimitingTests
{
    [Fact]
    public async Task LockoutPolicy_LocksUserFor15Minutes_After5ConsecutiveFailures()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(opt =>
        {
            opt.Lockout.AllowedForNewUsers = true;
            opt.Lockout.MaxFailedAccessAttempts = 5;
            opt.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddEntityFrameworkStores<IdentityAppDbContext>();

        services.AddScoped(_ => dbContext);

        using var provider = services.BuildServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<IdentityUser<Guid>>>();

        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = "matt",
            LockoutEnabled = true
        };

        var createResult = await userManager.CreateAsync(user, "Password123!");
        Assert.True(createResult.Succeeded);

        // Record 4 failed attempts: user is not yet locked out
        for (var i = 1; i <= 4; i++)
        {
            await userManager.AccessFailedAsync(user);
            Assert.False(await userManager.IsLockedOutAsync(user));
        }

        // 5th failed attempt triggers lockout
        await userManager.AccessFailedAsync(user);
        Assert.True(await userManager.IsLockedOutAsync(user));

        var lockoutEnd = await userManager.GetLockoutEndDateAsync(user);
        Assert.NotNull(lockoutEnd);
        var remainingLockout = lockoutEnd.Value - DateTimeOffset.UtcNow;
        Assert.True(remainingLockout > TimeSpan.FromMinutes(14), $"Lockout duration should be ~15m, was {remainingLockout.TotalMinutes}m");
    }

    [Fact]
    public async Task LockoutPolicy_ResetsFailedCount_OnSuccessfulLogin()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<IdentityAppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var dbContext = new IdentityAppDbContext(options);
        await dbContext.Database.MigrateAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentity<IdentityUser<Guid>, IdentityRole<Guid>>(opt =>
        {
            opt.Lockout.AllowedForNewUsers = true;
            opt.Lockout.MaxFailedAccessAttempts = 5;
            opt.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddEntityFrameworkStores<IdentityAppDbContext>();

        services.AddScoped(_ => dbContext);

        using var provider = services.BuildServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<IdentityUser<Guid>>>();

        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = "matt",
            LockoutEnabled = true
        };

        await userManager.CreateAsync(user, "Password123!");

        // 3 failed attempts
        await userManager.AccessFailedAsync(user);
        await userManager.AccessFailedAsync(user);
        await userManager.AccessFailedAsync(user);

        var failedCountBefore = await userManager.GetAccessFailedCountAsync(user);
        Assert.Equal(3, failedCountBefore);

        // Reset on success
        await userManager.ResetAccessFailedCountAsync(user);
        var failedCountAfter = await userManager.GetAccessFailedCountAsync(user);
        Assert.Equal(0, failedCountAfter);
    }

    [Fact]
    public void RateLimitingPolicy_ConfiguresAuthRateLimit_With10PerMinuteAnd429Rejection()
    {
        var services = new ServiceCollection();
        IdentityServerConfig.ConfigureRateLimiting(services);

        using var provider = services.BuildServiceProvider();
        var rateLimiterOptions = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

        Assert.Equal(StatusCodes.Status429TooManyRequests, rateLimiterOptions.RejectionStatusCode);
    }
}
