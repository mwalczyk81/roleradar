using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Demo;

/// <summary>
/// The demo guard fails closed. These cases use scratch values only; nothing here reads configuration,
/// user secrets or environment variables, and nothing connects to a database.
/// </summary>
public sealed class DemoModeGuardTests
{
    private const string DemoConnection = "mongodb://demo-mongo:27017/?directConnection=true";

    [Fact]
    public void CheckForDemoStack_DemoModeDemoDatabaseDemoHost_Allows()
    {
        var result = DemoModeGuard.CheckForDemoStack("1", DemoModeGuard.DemoDatabaseName, DemoConnection);

        Assert.True(result.Allowed);
    }

    [Theory]
    // The real database name, an empty name and near misses.
    [InlineData("1", "roleradar", DemoConnection)]
    [InlineData("1", "", DemoConnection)]
    [InlineData("1", null, DemoConnection)]
    [InlineData("1", "roleradar-demo2", DemoConnection)]
    [InlineData("1", "Roleradar-Demo", DemoConnection)]
    [InlineData("1", " roleradar-demo", DemoConnection)]
    // Demo mode off, missing or not exactly "1".
    [InlineData("0", "roleradar-demo", DemoConnection)]
    [InlineData(null, "roleradar-demo", DemoConnection)]
    [InlineData("", "roleradar-demo", DemoConnection)]
    [InlineData("true", "roleradar-demo", DemoConnection)]
    [InlineData("01", "roleradar-demo", DemoConnection)]
    // Hosts that are not the demo Mongo service: the developer's own server in every spelling.
    [InlineData("1", "roleradar-demo", "mongodb://127.0.0.1:27017/?replicaSet=rs0&directConnection=true")]
    [InlineData("1", "roleradar-demo", "mongodb://localhost:27017/")]
    [InlineData("1", "roleradar-demo", "mongodb://host.docker.internal:27017/")]
    [InlineData("1", "roleradar-demo", "mongodb://mongo:27017/?directConnection=true")]
    [InlineData("1", "roleradar-demo", "mongodb://demo-mongo:27017,demo-mongo:27018/")]
    [InlineData("1", "roleradar-demo", "mongodb://demo-mongo-2:27017/")]
    // No usable connection string.
    [InlineData("1", "roleradar-demo", null)]
    [InlineData("1", "roleradar-demo", "")]
    [InlineData("1", "roleradar-demo", "   ")]
    [InlineData("1", "roleradar-demo", "not a connection string")]
    public void CheckForDemoStack_Refuses(string? demoMode, string? database, string? connectionString)
    {
        var result = DemoModeGuard.CheckForDemoStack(demoMode, database, connectionString);

        Assert.False(result.Allowed);
        AssertMessageEchoesNothing(result.Message, connectionString);
    }

    [Fact]
    public void Check_WithExplicitAllowedHost_AllowsOnlyThatHost()
    {
        // The explicit-host overload exists for tests that run against a Testcontainers endpoint.
        const string connection = "mongodb://scratch-host:41234/?directConnection=true";

        Assert.True(DemoModeGuard.Check("1", DemoModeGuard.DemoDatabaseName, connection, "scratch-host").Allowed);
        Assert.False(DemoModeGuard.Check("1", DemoModeGuard.DemoDatabaseName, connection, "other-host").Allowed);
        Assert.False(DemoModeGuard.Check("1", DemoModeGuard.DemoDatabaseName, connection, "").Allowed);
        Assert.False(DemoModeGuard.Check("1", "roleradar", connection, "scratch-host").Allowed);
        Assert.False(DemoModeGuard.Check("0", DemoModeGuard.DemoDatabaseName, connection, "scratch-host").Allowed);
    }

    [Theory]
    // Demo mode on: must be the demo database on the demo host.
    [InlineData("1", "roleradar-demo", DemoConnection, true)]
    [InlineData("1", "roleradar", DemoConnection, false)]
    [InlineData("1", "roleradar-demo", "mongodb://127.0.0.1:27017/", false)]
    // The demo database is never served as a real one.
    [InlineData(null, "roleradar-demo", DemoConnection, false)]
    [InlineData("", "roleradar-demo", DemoConnection, false)]
    [InlineData("0", "roleradar-demo", DemoConnection, false)]
    [InlineData("true", "roleradar-demo", DemoConnection, false)]
    // Ordinary configurations are not touched.
    [InlineData(null, "roleradar", "mongodb://127.0.0.1:27017/", true)]
    [InlineData("", "roleradar", "mongodb://mongo:27017/", true)]
    [InlineData("0", "roleradar", "mongodb://mongo:27017/", true)]
    [InlineData(null, "rrtest_abc", "mongodb://127.0.0.1:55000/", true)]
    // An ambiguous switch value fails closed even for another database.
    [InlineData("true", "roleradar", "mongodb://127.0.0.1:27017/", false)]
    [InlineData("yes", "roleradar", "mongodb://mongo:27017/", false)]
    public void CheckServe_DecidesWhetherTheHostMayStart(string? demoMode, string? database, string? connectionString, bool expected)
    {
        var result = DemoModeGuard.CheckServe(demoMode, database, connectionString);

        Assert.Equal(expected, result.Allowed);
        if (!expected)
        {
            AssertMessageEchoesNothing(result.Message, connectionString);
        }
    }

    [Fact]
    public void Constants_AreTheDocumentedNonSecretValues()
    {
        Assert.Equal("ROLERADAR_DEMO", DemoModeGuard.DemoModeVariable);
        Assert.Equal("roleradar-demo", DemoModeGuard.DemoDatabaseName);
        Assert.Equal("demo-mongo", DemoModeGuard.DemoMongoHost);
    }

    private static void AssertMessageEchoesNothing(string message, string? connectionString)
    {
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("mongodb://", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", message, StringComparison.Ordinal);
        Assert.DoesNotContain("host.docker.internal", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("localhost", message, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.DoesNotContain(connectionString, message, StringComparison.Ordinal);
        }
    }
}
