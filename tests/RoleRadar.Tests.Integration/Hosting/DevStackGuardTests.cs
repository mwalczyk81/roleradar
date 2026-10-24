using RoleRadar.Tests.Integration.Hosting;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

public sealed class DevStackGuardTests
{
    [Fact]
    public void SharedHarness_ValidateNotDevStack_RejectsDevStackEndpoints()
    {
        Assert.Throws<InvalidOperationException>(() => ContainerTestHarness.ValidateNotDevStack("mongodb://127.0.0.1:27017"));
        Assert.Throws<InvalidOperationException>(() => ContainerTestHarness.ValidateNotDevStack("mongodb://localhost:27017"));
        Assert.Throws<InvalidOperationException>(() => ContainerTestHarness.ValidateNotDevStack("mongodb://host.docker.internal:27017"));
    }

    [Fact]
    public void SharedHarness_ValidateNotDevStack_AllowsOverride()
    {
        try
        {
            Environment.SetEnvironmentVariable("ROLERADAR_ALLOW_DEV_STACK", "1");
            var ex = Record.Exception(() => ContainerTestHarness.ValidateNotDevStack("mongodb://127.0.0.1:27017"));
            Assert.Null(ex);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROLERADAR_ALLOW_DEV_STACK", null);
        }
    }

    [Fact]
    public void SharedHarness_ValidateNotDevStack_AllowsEphemeralPorts()
    {
        var ex = Record.Exception(() => ContainerTestHarness.ValidateNotDevStack("mongodb://127.0.0.1:58432"));
        Assert.Null(ex);
    }
}
