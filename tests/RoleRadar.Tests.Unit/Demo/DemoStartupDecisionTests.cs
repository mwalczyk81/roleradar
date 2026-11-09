using RoleRadar.Api;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Demo;

/// <summary>
/// The API refuses to start when demo mode and the database disagree, and never serves the demo database as real
/// (FR-004, SC-003). The decision itself is <see cref="DemoModeGuard.CheckServe"/>; these tests pin the start-up wiring.
/// </summary>
public sealed class DemoStartupDecisionTests
{
    private const string DemoConnection = "mongodb://demo-mongo:27017/?directConnection=true";
    private const string RealConnection = "mongodb://127.0.0.1:27017/?replicaSet=rs0&directConnection=true";

    [Theory]
    [InlineData("1", "roleradar-demo", DemoConnection)]
    [InlineData(null, "roleradar", RealConnection)]
    [InlineData("", "roleradar", "mongodb://mongo:27017/?directConnection=true")]
    [InlineData("0", "rrtest_abc", RealConnection)]
    public void Enforce_AllowsTheDemoStackAndEveryOrdinaryConfiguration(string? mode, string database, string connection)
    {
        DemoStartupCheck.Enforce(mode, database, connection);
    }

    [Theory]
    [InlineData("1", "roleradar", DemoConnection)]
    [InlineData("1", "roleradar-demo", RealConnection)]
    [InlineData(null, "roleradar-demo", DemoConnection)]
    [InlineData("0", "roleradar-demo", DemoConnection)]
    [InlineData("true", "roleradar", RealConnection)]
    public void Enforce_RefusesToStart_WithoutEchoingTheConnection(string? mode, string database, string connection)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DemoStartupCheck.Enforce(mode, database, connection));

        Assert.StartsWith("[FATAL] RoleRadar.Api refused to start", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mongodb://", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiProgram_CallsTheStartupCheckWithTheGuardsSwitchName()
    {
        var root = FindRepoRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "RoleRadar.Api", "Program.cs"));

        Assert.Contains("DemoStartupCheck.Enforce(", program, StringComparison.Ordinal);
        Assert.Contains("DemoModeGuard.DemoModeVariable", program, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
