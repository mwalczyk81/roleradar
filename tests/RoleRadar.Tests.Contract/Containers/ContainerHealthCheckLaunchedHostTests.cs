using Xunit;

namespace RoleRadar.Tests.Contract.Containers;

public sealed class ContainerHealthCheckLaunchedHostTests
{
    [Fact]
    public async Task IdentityAndApi_HealthCheckFlag_ReturnsOneWhenEndpointUnreachable()
    {
        var previousUrl = Environment.GetEnvironmentVariable("ROLERADAR_HEALTHCHECK_URL");
        try
        {
            // Point to an unused loopback port where nothing is listening
            Environment.SetEnvironmentVariable("ROLERADAR_HEALTHCHECK_URL", "http://127.0.0.1:59999/health");

            var identityExit = await RoleRadar.Identity.Program.Main(["--healthcheck"]);
            Assert.Equal(1, identityExit);

            var apiExit = await RoleRadar.Api.Program.Main(["--healthcheck"]);
            Assert.Equal(1, apiExit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROLERADAR_HEALTHCHECK_URL", previousUrl);
        }
    }
}
