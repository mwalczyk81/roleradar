using RoleRadar.Tests.Integration.Hosting;
using Xunit;

[assembly: AssemblyFixture(typeof(RoleRadar.Tests.Integration.Fixtures.ContainerImagesFixture))]

namespace RoleRadar.Tests.Integration.Fixtures;

/// <summary>
/// Pre-builds both container images (CLI and MCP) at assembly/collection initialization
/// before any benchmark tests run, ensuring image build never overlaps timed runs.
/// </summary>
public sealed class ContainerImagesFixture : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
