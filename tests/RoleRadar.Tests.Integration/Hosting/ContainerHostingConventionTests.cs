using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

public sealed class ContainerHostingConventionTests
{
    private const string NonLoopbackUnreachableCredentialedUri =
        "mongodb://testuser:supersecretpass@192.0.2.1:27017/rrtest?connectTimeoutMS=1000";

    [Fact]
    public async Task Cli_ConfiguredStrictlyViaEnvironment_MasksCredentialsInErrorMessages()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();

        var env = new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = NonLoopbackUnreachableCredentialedUri
        };

        var result = await ContainerTestHarness.RunCliAsync(["status", "--json"], environment: env);

        Assert.Equal(1, result.ExitCode);
        Assert.DoesNotContain("supersecretpass", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("testuser", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("supersecretpass", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("testuser", result.StandardError, StringComparison.OrdinalIgnoreCase);

        // Standard error must contain redacted endpoint
        Assert.Contains("192.0.2.1:27017", result.StandardError);
    }

    [Fact]
    public async Task Mcp_DiagnosticLogging_RoutesExclusivelyToStderr_KeepingStdoutPureJsonRpc()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();

        // Standard initialize request
        const string initializeRpc = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2024-11-05\",\"capabilities\":{},\"clientInfo\":{\"name\":\"test\",\"version\":\"1.0\"}}}\n";

        var result = await ContainerTestHarness.RunMcpAsync(initializeRpc);

        Assert.Equal(0, result.ExitCode);
        Assert.NotEmpty(result.StandardOutput);

        // Verify stdout contains ONLY valid JSON-RPC frames (every non-empty line must parse as json with jsonrpc: 2.0)
        var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.True(doc.RootElement.TryGetProperty("jsonrpc", out var rpcVersion));
            Assert.Equal("2.0", rpcVersion.GetString());
        }

        // Stdout must never contain log levels or info/warn messages
        Assert.DoesNotContain("info:", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("warn:", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fail:", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Container_UnderNonUtcTimezone_EmitsStrictUtcTimestamps()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();

        // Execute status --json with non-UTC timezone
        var env = new Dictionary<string, string>
        {
            ["TZ"] = "America/Chicago",
            ["ROLERADAR_MONGO_CONNECTION_STRING"] = NonLoopbackUnreachableCredentialedUri
        };

        var result = await ContainerTestHarness.RunCliAsync(["status", "--json"], environment: env);

        Assert.Equal(1, result.ExitCode);
        // Verify no local time offset like -05:00 or -06:00 is emitted in JSON stdout
        Assert.DoesNotContain("-05:00", result.StandardOutput);
        Assert.DoesNotContain("-06:00", result.StandardOutput);
    }

    [Fact]
    public async Task McpContainer_ReceivesSigterm_TerminatesWithinBoundedTimeout()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();

        var containerName = $"test-sigterm-mcp-{Guid.NewGuid():N}";
        var image = ContainerTestHarness.ResolveMcpImage();

        // Start interactive container in background
        var startPsi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"run -d -i --name {containerName} --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m --cap-drop ALL --security-opt no-new-privileges {image}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var startProcess = Process.Start(startPsi)!;
        await startProcess.WaitForExitAsync();
        Assert.Equal(0, startProcess.ExitCode);

        try
        {
            // Allow container to boot
            await Task.Delay(1000);

            var stopwatch = Stopwatch.StartNew();

            // Send SIGTERM
            var killPsi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"kill -s SIGTERM {containerName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var killProcess = Process.Start(killPsi)!;
            await killProcess.WaitForExitAsync();

            // Wait for exit
            var waitPsi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"wait {containerName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var waitProcess = Process.Start(waitPsi)!;
            await waitProcess.WaitForExitAsync();
            stopwatch.Stop();

            var exitCodeStr = (await waitProcess.StandardOutput.ReadToEndAsync()).Trim();
            _ = int.TryParse(exitCodeStr, out var exitCode);

            // Container must exit cleanly within 5 seconds upon SIGTERM
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5.0), $"SIGTERM shutdown took {stopwatch.Elapsed.TotalSeconds:F2}s, exceeding 5.0s bound.");
            // Common exit codes for clean or SIGTERM termination: 0 or 143 (128 + 15)
            Assert.True(exitCode == 0 || exitCode == 143, $"Unexpected exit code upon SIGTERM: {exitCode}");
        }
        finally
        {
            var rmPsi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"rm -f {containerName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var rmProcess = Process.Start(rmPsi);
            if (rmProcess != null)
            {
                await rmProcess.WaitForExitAsync();
            }
        }
    }

    [Fact]
    public async Task CliContainer_ReceivesSigterm_TerminatesWithinBoundedTimeout()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();

        var containerName = $"test-sigterm-cli-{Guid.NewGuid():N}";
        var image = ContainerTestHarness.ResolveCliImage();

        // Start container running a long-running or interactive command in background
        var startPsi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"run -d -i --name {containerName} --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m --cap-drop ALL --security-opt no-new-privileges -e ROLERADAR_MONGO_CONNECTION_STRING=\"{NonLoopbackUnreachableCredentialedUri}\" {image} status",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var startProcess = Process.Start(startPsi)!;
        await startProcess.WaitForExitAsync();
        Assert.Equal(0, startProcess.ExitCode);

        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Send SIGTERM immediately while probe or command is active
            var killPsi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"kill -s SIGTERM {containerName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var killProcess = Process.Start(killPsi)!;
            await killProcess.WaitForExitAsync();

            // Wait for exit
            var waitPsi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"wait {containerName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var waitProcess = Process.Start(waitPsi)!;
            await waitProcess.WaitForExitAsync();
            stopwatch.Stop();

            var exitCodeStr = (await waitProcess.StandardOutput.ReadToEndAsync()).Trim();
            _ = int.TryParse(exitCodeStr, out var exitCode);

            // Container must terminate cleanly within 5 seconds upon SIGTERM
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5.0), $"SIGTERM shutdown took {stopwatch.Elapsed.TotalSeconds:F2}s, exceeding 5.0s bound.");
            Assert.True(exitCode == 0 || exitCode == 1 || exitCode == 143, $"Unexpected exit code upon SIGTERM: {exitCode}");
        }
        finally
        {
            var rmPsi = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"rm -f {containerName}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var rmProcess = Process.Start(rmPsi);
            if (rmProcess != null)
            {
                await rmProcess.WaitForExitAsync();
            }
        }
    }
}
