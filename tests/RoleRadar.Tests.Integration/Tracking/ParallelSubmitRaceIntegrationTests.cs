namespace RoleRadar.Tests.Integration.Tracking;

// T067 Reference alias linking task T067 directly to the comprehensive shipped host race suite
public static class ParallelSubmitRaceIntegrationTests
{
    public static readonly Type TargetSuite = typeof(ShippedHostRaceIntegrationTests);
}
