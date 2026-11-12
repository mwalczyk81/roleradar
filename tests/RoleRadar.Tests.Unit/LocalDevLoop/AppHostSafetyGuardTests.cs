using RoleRadar.AppHost;
using Xunit;

namespace RoleRadar.Tests.Unit.LocalDevLoop;

/// <summary>
/// Feature 015 Gate 5 (SC-006): the AppHost's fail-closed guard. It must refuse, before any resource starts, anything that
/// could reach the developer's real stack (names, volumes, ports, connection strings) and any host port already bound.
/// The guard and the settings are plain files with no Aspire dependency, so these tests link them in as source.
/// </summary>
public sealed class AppHostSafetyGuardTests
{
    private static readonly Func<int, bool> NothingBound = static _ => false;

    private static AppHostPlan PlanWith(
        string[]? names = null,
        string[]? volumes = null,
        int[]? ports = null,
        string[]? connectionStrings = null)
    {
        var defaults = AppHostSettings.CreatePlan();
        return new AppHostPlan(
            names ?? defaults.Names,
            volumes ?? defaults.VolumeNames,
            ports ?? defaults.HostPorts,
            connectionStrings ?? defaults.ConnectionStrings);
    }

    [Fact]
    public void TheDefaultPlan_PassesWhenNoHostPortIsBound()
    {
        AppHostSafetyGuard.AssertIsolatedAppHostEnvironment(AppHostSettings.CreatePlan(), NothingBound);
    }

    [Fact]
    public void TheDefaultPlan_UsesOnlyThrowawayNamesAndDistinctPortsOutsideTheRealStack()
    {
        var plan = AppHostSettings.CreatePlan();

        Assert.All(plan.Names.Concat(plan.VolumeNames), n => Assert.StartsWith("roleradar-apphost-", n, StringComparison.Ordinal));
        Assert.Equal(plan.HostPorts.Count, plan.HostPorts.Distinct().Count());
        Assert.Empty(plan.HostPorts.Intersect([27017, 5001, 5002, 5003]));
        Assert.All(plan.HostPorts, p => Assert.InRange(p, 15000, 65535));
        Assert.NotEmpty(plan.VolumeNames);
        Assert.DoesNotContain(plan.ConnectionStrings, c => c.Contains(":27017", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(27017)]
    [InlineData(5001)]
    [InlineData(5002)]
    [InlineData(5003)]
    public void ARealStackHostPort_IsRefused(int port)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            AppHostSafetyGuard.AssertIsolatedAppHostEnvironment(PlanWith(ports: [15001, port]), NothingBound));

        Assert.Contains(port.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("roleradar-mongo")]
    [InlineData("roleradar-mongo-data")]
    [InlineData("roleradar-identity-data")]
    public void ARealStackName_IsRefused_AsAResourceOrAsAVolume(string name)
    {
        Assert.Throws<InvalidOperationException>(() =>
            AppHostSafetyGuard.AssertIsolatedAppHostEnvironment(PlanWith(names: ["roleradar-apphost-api", name]), NothingBound));
        Assert.Throws<InvalidOperationException>(() =>
            AppHostSafetyGuard.AssertIsolatedAppHostEnvironment(PlanWith(volumes: [name]), NothingBound));
    }

    [Theory]
    [InlineData("api")]
    [InlineData("roleradar-api")]
    [InlineData("deploy-api-1")]
    public void ANameOutsideTheThrowawayPrefix_IsRefused(string name)
    {
        Assert.Throws<InvalidOperationException>(() =>
            AppHostSafetyGuard.AssertIsolatedAppHostEnvironment(PlanWith(names: [name]), NothingBound));
    }

    [Theory]
    [InlineData("mongodb://127.0.0.1:27017/?directConnection=true")]
    [InlineData("mongodb://localhost:27017")]
    [InlineData("mongodb://roleradar-mongo:37017/?directConnection=true")]
    public void AConnectionStringForTheRealMongo_IsRefused_WithoutEchoingTheString(string connectionString)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            AppHostSafetyGuard.AssertIsolatedAppHostEnvironment(PlanWith(connectionStrings: [connectionString]), NothingBound));

        Assert.DoesNotContain(connectionString, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AHostPortThatIsAlreadyBound_IsRefused_AndNamesThePort()
    {
        var plan = AppHostSettings.CreatePlan();
        var bound = plan.HostPorts.First();

        var error = Assert.Throws<InvalidOperationException>(() =>
            AppHostSafetyGuard.AssertIsolatedAppHostEnvironment(plan, port => port == bound));

        Assert.Contains(bound.ToString(System.Globalization.CultureInfo.InvariantCulture), error.Message, StringComparison.Ordinal);
        Assert.Contains("already in use", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDefaultPortProbe_SeesARealListener()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;

        Assert.True(AppHostSafetyGuard.IsHostPortBound(port));
    }

    [Fact]
    public void TheGuard_ChecksEveryReason_NotJustTheFirst()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            AppHostSafetyGuard.AssertIsolatedAppHostEnvironment(
                PlanWith(names: ["roleradar-mongo"], volumes: ["roleradar-identity-data"], ports: [27017]), NothingBound));

        Assert.Contains("roleradar-mongo", error.Message, StringComparison.Ordinal);
        Assert.Contains("roleradar-identity-data", error.Message, StringComparison.Ordinal);
        Assert.Contains("27017", error.Message, StringComparison.Ordinal);
    }
}
