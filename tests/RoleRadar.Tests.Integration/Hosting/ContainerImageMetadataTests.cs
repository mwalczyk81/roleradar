using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

public sealed class ContainerImageMetadataTests
{
    private static async Task<JsonElement> InspectImageConfigAsync(string imageTag)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"image inspect {imageTag}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to run docker inspect on {imageTag}");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);

        using var doc = JsonDocument.Parse(stdout);
        return doc.RootElement[0].GetProperty("Config").Clone();
    }

    [Theory]
    [InlineData("cli")]
    [InlineData("mcp")]
    public async Task ContainerImages_MustRunAsNonRootUser1654(string target)
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();
        var imageTag = target == "cli"
            ? ContainerTestHarness.ResolveCliImage()
            : ContainerTestHarness.ResolveMcpImage();

        var config = await InspectImageConfigAsync(imageTag);
        var user = config.GetProperty("User").GetString();

        Assert.True(
            user == "1654" || user == "1654:1654" || user == "app",
            $"Image {imageTag} must run as non-root user 1654 (got '{user}').");
    }

    [Theory]
    [InlineData("cli")]
    [InlineData("mcp")]
    public async Task ContainerImages_MustHaveZeroExposedPorts(string target)
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();
        var imageTag = target == "cli"
            ? ContainerTestHarness.ResolveCliImage()
            : ContainerTestHarness.ResolveMcpImage();

        var config = await InspectImageConfigAsync(imageTag);

        if (config.TryGetProperty("ExposedPorts", out var exposedPorts) &&
            exposedPorts.ValueKind != JsonValueKind.Null)
        {
            var count = exposedPorts.EnumerateObject().Count();
            Assert.True(count == 0, $"Image {imageTag} must have 0 exposed ports, but found {count}.");
        }
    }

    [Theory]
    [InlineData("cli")]
    [InlineData("mcp")]
    public async Task ContainerImages_MustNotContainShell(string target)
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();
        var imageTag = target == "cli"
            ? ContainerTestHarness.ResolveCliImage()
            : ContainerTestHarness.ResolveMcpImage();

        // Invoking /bin/sh must fail
        var shResult = await ContainerTestHarness.RunRawAsync(
            imageTag,
            ["-c", "echo exploit"],
            entrypoint: "/bin/sh");

        Assert.NotEqual(0, shResult.ExitCode);

        // Invoking /bin/bash must fail
        var bashResult = await ContainerTestHarness.RunRawAsync(
            imageTag,
            ["-c", "echo exploit"],
            entrypoint: "/bin/bash");

        Assert.NotEqual(0, bashResult.ExitCode);
    }

    [Theory]
    [InlineData("cli")]
    [InlineData("mcp")]
    public async Task ContainerImages_MustNotContainPackageManager(string target)
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();
        var imageTag = target == "cli"
            ? ContainerTestHarness.ResolveCliImage()
            : ContainerTestHarness.ResolveMcpImage();

        // Invoking apt or dpkg must fail
        var aptResult = await ContainerTestHarness.RunRawAsync(
            imageTag,
            ["--version"],
            entrypoint: "apt");

        Assert.NotEqual(0, aptResult.ExitCode);

        var dpkgResult = await ContainerTestHarness.RunRawAsync(
            imageTag,
            ["--version"],
            entrypoint: "dpkg");

        Assert.NotEqual(0, dpkgResult.ExitCode);
    }
}
