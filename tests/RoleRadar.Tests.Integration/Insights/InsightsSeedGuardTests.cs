namespace RoleRadar.Tests.Integration.Insights;

using Xunit;

/// <summary>No Docker needed: proves the guard accepts only the test's own container endpoint and throwaway name.</summary>
public sealed class InsightsSeedGuardTests
{
    private const string Host = "127.0.0.1";
    private const int Port = 49321;

    private static readonly string GoodName = InsightsSeedGuard.NewDatabaseName();

    [Fact]
    public void Accepts_The_Container_Endpoint_And_A_Throwaway_Name()
    {
        InsightsSeedGuard.AssertIsolated($"mongodb://root:pw@{Host}:{Port}/?directConnection=true", GoodName, Host, Port);
    }

    [Theory]
    [InlineData("mongodb://localhost:27017")]
    [InlineData("mongodb://127.0.0.1:27017/?directConnection=true")]
    [InlineData("mongodb://127.0.0.1:49322")]
    [InlineData("mongodb://other-host.example:49321")]
    [InlineData("mongodb://127.0.0.1:49321,127.0.0.1:49322")]
    public void Rejects_Any_Endpoint_That_Is_Not_The_Test_Container(string connectionString)
    {
        Assert.Throws<InvalidOperationException>(() =>
            InsightsSeedGuard.AssertIsolated(connectionString, GoodName, Host, Port));
    }

    [Theory]
    [InlineData("roleradar")]
    [InlineData("insights-parity-")]
    [InlineData("insights-parity-not-hex-not-hex-not-hex-xxxx")]
    [InlineData("Insights-Parity-0123456789abcdef0123456789abcdef")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_A_Database_Name_Without_The_Throwaway_Prefix(string? databaseName)
    {
        Assert.Throws<InvalidOperationException>(() =>
            InsightsSeedGuard.AssertIsolated($"mongodb://{Host}:{Port}", databaseName, Host, Port));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a connection string")]
    public void Rejects_A_Missing_Or_Unparseable_Connection_String(string? connectionString)
    {
        Assert.Throws<InvalidOperationException>(() =>
            InsightsSeedGuard.AssertIsolated(connectionString, GoodName, Host, Port));
    }

    [Fact]
    public void Failure_Messages_Never_Echo_The_Offending_Host()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            InsightsSeedGuard.AssertIsolated("mongodb://real-server.invalid:27017", GoodName, Host, Port));
        Assert.DoesNotContain("real-server", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
