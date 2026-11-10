using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Observability;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

/// <summary>
/// Feature 013 US3 (FR-023, contracts/health.md): the identity readiness check reports the identity database, bounded
/// at 2 s. It is built from the real check and a real SQLite context, with no stub standing in for the check itself.
/// </summary>
public sealed class IdentityReadinessCheckTests : IDisposable
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(2);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rrtest_identity_ready_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static IdentityAppDbContext ContextAt(string path) =>
        new(new DbContextOptionsBuilder<IdentityAppDbContext>().UseSqlite($"Data Source={path}").Options);

    private static async Task<HealthCheckResult> RunAsync(IdentityReadinessCheck check)
    {
        var context = new HealthCheckContext
        {
            Registration = new HealthCheckRegistration("identity-ready", check, failureStatus: null, tags: ["ready"])
        };
        return await check.CheckHealthAsync(context, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AnUnopenablePath_IsUnhealthy_WithinTheTwoSecondBound()
    {
        // A file inside a directory that does not exist cannot be opened or created.
        await using var db = ContextAt(Path.Combine(_directory, "missing", "identity.db"));

        var started = Stopwatch.StartNew();
        var result = await RunAsync(new IdentityReadinessCheck(db));
        started.Stop();

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.True(started.Elapsed < Bound + TimeSpan.FromMilliseconds(500), $"Took {started.Elapsed.TotalMilliseconds:0} ms; the bound is 2000 ms.");
    }

    [Fact]
    public async Task AnOpenableDatabase_IsHealthy()
    {
        Directory.CreateDirectory(_directory);
        await using var db = ContextAt(Path.Combine(_directory, "identity.db"));
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        var result = await RunAsync(new IdentityReadinessCheck(db));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task TheResult_CarriesNoPathOrExceptionText()
    {
        await using var db = ContextAt(Path.Combine(_directory, "missing", "identity.db"));

        var result = await RunAsync(new IdentityReadinessCheck(db));

        Assert.Null(result.Exception);
        Assert.DoesNotContain("rrtest_identity_ready_", result.Description ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(result.Data);
    }
}
