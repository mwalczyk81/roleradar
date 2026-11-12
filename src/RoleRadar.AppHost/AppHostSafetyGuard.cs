using System.Globalization;
using System.Net.NetworkInformation;

namespace RoleRadar.AppHost;

/// <summary>What the loop is about to create: resource names, volumes, host ports and connection strings.</summary>
internal sealed record AppHostPlan(
    IReadOnlyCollection<string> Names,
    IReadOnlyCollection<string> VolumeNames,
    IReadOnlyCollection<int> HostPorts,
    IReadOnlyCollection<string> ConnectionStrings);

/// <summary>
/// Fail-closed guard (feature 015 Gate 5, SC-006). It runs before the distributed application is even built and refuses to
/// start when the plan could touch the developer's real stack: the real Mongo container, its data volumes, the host ports
/// the real stack publishes, or a host port that is already bound by anything (which would mean another stack is running).
/// It lists every reason it finds, and never echoes a connection string.
/// </summary>
internal static class AppHostSafetyGuard
{
    private static readonly string[] RealStackNames = ["roleradar-mongo", "roleradar-mongo-data", "roleradar-identity-data"];

    // 27017 is the real Mongo; 5001 to 5003 are the real Identity, API and web.
    private static readonly int[] RealStackPorts = [27017, 5001, 5002, 5003];

    public static void AssertIsolatedAppHostEnvironment(AppHostPlan plan, Func<int, bool>? isPortBound = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        isPortBound ??= IsHostPortBound;

        var problems = new List<string>();

        foreach (var name in plan.Names.Concat(plan.VolumeNames))
        {
            if (RealStackNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                problems.Add($"'{name}' is a real-stack name");
            }
            else if (!name.StartsWith(AppHostSettings.ResourcePrefix, StringComparison.Ordinal))
            {
                problems.Add($"'{name}' does not start with the throwaway prefix '{AppHostSettings.ResourcePrefix}'");
            }
        }

        foreach (var port in plan.HostPorts)
        {
            if (RealStackPorts.Contains(port))
            {
                problems.Add($"host port {port.ToString(CultureInfo.InvariantCulture)} belongs to the real stack");
            }
        }

        foreach (var duplicate in plan.HostPorts.GroupBy(p => p).Where(g => g.Count() > 1))
        {
            problems.Add($"host port {duplicate.Key.ToString(CultureInfo.InvariantCulture)} is used twice");
        }

        var index = 0;
        foreach (var connectionString in plan.ConnectionStrings)
        {
            index++;
            if (connectionString.Contains(":27017", StringComparison.Ordinal)
                || RealStackNames.Any(n => connectionString.Contains(n, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add($"connection string #{index.ToString(CultureInfo.InvariantCulture)} points at the real Mongo (port 27017 or a real-stack name)");
            }
        }

        foreach (var port in plan.HostPorts.Distinct().Where(p => !RealStackPorts.Contains(p)))
        {
            if (isPortBound(port))
            {
                problems.Add($"host port {port.ToString(CultureInfo.InvariantCulture)} is already in use; stop whatever listens there first");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                "RoleRadar.AppHost refused to start, nothing was launched: " + string.Join("; ", problems) + ".");
        }
    }

    /// <summary>True when any TCP listener, on any address, already uses the port.</summary>
    public static bool IsHostPortBound(int port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint => endpoint.Port == port);
}
