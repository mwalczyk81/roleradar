using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

[Trait("Lane", "images")]
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
            // Wait until the process can actually catch SIGTERM (a real condition, not a fixed delay: under a slow Docker
            // daemon one second is not enough, and a signal sent before the handler exists is dropped by PID 1).
            await ContainerSignalReadiness.WaitUntilSigtermCaughtAsync(containerName, TimeSpan.FromSeconds(60));

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

            // The 5 s bound is on the process's shutdown. Start timing once the signal has been sent: the time the Docker
            // daemon takes to answer the kill call itself (seconds on a saturated host) is not the application's.
            var stopwatch = Stopwatch.StartNew();

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
            var diagnostics = await ContainerSignalReadiness.DescribeAsync(containerName);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5.0), $"SIGTERM shutdown took {stopwatch.Elapsed.TotalSeconds:F2}s, exceeding 5.0s bound. {diagnostics}");
            // Common exit codes for clean or SIGTERM termination: 0 or 143 (128 + 15)
            Assert.True(exitCode == 0 || exitCode == 143, $"Unexpected exit code upon SIGTERM: {exitCode}. {diagnostics}");
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

        // Start an interactive command in the background: `backup --passphrase-stdin` blocks reading the passphrase from the
        // open stdin and never touches the database, so it cannot end by itself. It can only exit if SIGTERM is handled.
        // (The earlier `status` command ended on its own after 3 to 6 s, which let the test pass with SIGTERM ignored.)
        var startPsi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"run -d -i --name {containerName} --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m --cap-drop ALL --security-opt no-new-privileges -e ROLERADAR_MONGO_CONNECTION_STRING=\"{NonLoopbackUnreachableCredentialedUri}\" {image} backup --passphrase-stdin",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var startProcess = Process.Start(startPsi)!;
        await startProcess.WaitForExitAsync();
        Assert.Equal(0, startProcess.ExitCode);

        try
        {
            // A signal sent to PID 1 before its process can catch it is dropped. Wait for the real condition (the
            // handler is installed) instead of signalling right after `docker run -d` returns; a slow Docker daemon
            // or host stretches that start-up from tenths of a second to many seconds.
            await ContainerSignalReadiness.WaitUntilSigtermCaughtAsync(containerName, TimeSpan.FromSeconds(60));

            // Send SIGTERM now that the process can catch it
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

            // The 5 s bound is on the process's shutdown. Start timing once the signal has been sent: the time the Docker
            // daemon takes to answer the kill call itself (seconds on a saturated host) is not the application's.
            var stopwatch = Stopwatch.StartNew();

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

            // Container must terminate within 5 seconds upon SIGTERM, with 0 or 143 (128 + 15). Exit code 1 is no longer
            // accepted: it meant the command ended by itself, that is, SIGTERM had not been handled.
            var diagnostics = await ContainerSignalReadiness.DescribeAsync(containerName);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5.0), $"SIGTERM shutdown took {stopwatch.Elapsed.TotalSeconds:F2}s, exceeding 5.0s bound. {diagnostics}");
            Assert.True(exitCode == 0 || exitCode == 143, $"Unexpected exit code upon SIGTERM: {exitCode}. {diagnostics}");
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
