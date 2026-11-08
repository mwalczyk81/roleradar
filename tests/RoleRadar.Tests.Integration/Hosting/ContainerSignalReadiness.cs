using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RoleRadar.Tests.Integration.Hosting;

/// <summary>
/// Waits until a container's PID 1 can catch SIGTERM, a real condition, instead of signalling right after
/// <c>docker run -d</c> returns. A signal sent to PID 1 before its process has installed a handler is dropped, because the
/// kernel gives PID 1 no default action for it; the container then runs to its own end (exit code 1 for the CLI's
/// <c>status</c>) and a "terminates within a bound after SIGTERM" test fails, only when the Docker daemon or host is slow
/// enough to stretch process start-up (feature 010, research R8: 7 of 80 loaded runs, 0 of 20 idle).
/// The probe reads <c>SigCgt</c> in <c>/proc/1/status</c> through a throw-away helper container that shares the target's PID
/// namespace, so it works the same on Windows, macOS and Linux hosts and needs no shell in the (chiseled) image.
/// </summary>
internal static class ContainerSignalReadiness
{
    private static readonly Lazy<string> DockerfileHelperImage = new(ReadHelperImageFromDockerfile);

    /// <summary>
    /// The ubuntu image the Dockerfile pins for its <c>tools-downloader</c> stage, read from the Dockerfile at run time so the
    /// digest lives in one place: a Dependabot bump of that stage cannot leave a second copy behind and break this test.
    /// </summary>
    public static string HelperImage => DockerfileHelperImage.Value;

    private static string ReadHelperImageFromDockerfile()
    {
        var dockerfile = File.ReadAllText(Path.Combine(ContainerTestHarness.FindRepoRoot(), "Dockerfile"));
        var match = Regex.Match(dockerfile, @"FROM (ubuntu:[\w.]+@sha256:[0-9a-f]{64}) AS tools-downloader");
        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException("the Dockerfile tools-downloader stage no longer names a pinned ubuntu image");
    }

    /// <summary>SIGTERM is signal 15; bit n-1 of the SigCgt mask stands for signal n.</summary>
    private const ulong SigtermBit = 1UL << 14;

    public static bool IsSigtermCaught(string procStatus)
    {
        foreach (var line in procStatus.Split('\n'))
        {
            if (!line.StartsWith("SigCgt:", StringComparison.Ordinal))
            {
                continue;
            }

            var hex = line["SigCgt:".Length..].Trim();
            return ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var mask) && (mask & SigtermBit) != 0;
        }

        return false;
    }

    public static async Task WaitUntilSigtermCaughtAsync(string containerName, TimeSpan timeout)
    {
        var deadline = Stopwatch.StartNew();
        var last = "(no probe finished)";

        while (deadline.Elapsed < timeout)
        {
            var (exit, output) = await RunDockerAsync(
                "run", "--rm", "--pid", $"container:{containerName}", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                HelperImage, "cat", "/proc/1/status");

            if (exit == 0 && IsSigtermCaught(output))
            {
                return;
            }

            if (output.Contains("is not running", StringComparison.OrdinalIgnoreCase) || output.Contains("No such container", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Container {containerName} stopped before it could be signalled, so the SIGTERM test is inconclusive. {await DescribeAsync(containerName)}");
            }

            last = exit == 0 ? "SigCgt had no SIGTERM bit yet" : output.Trim();
            await Task.Delay(100);
        }

        throw new TimeoutException($"Container {containerName} did not install a SIGTERM handler within {timeout.TotalSeconds:F0}s ({last}). {await DescribeAsync(containerName)}");
    }

    /// <summary>State and the last log lines, for a failure message that explains itself.</summary>
    public static async Task<string> DescribeAsync(string containerName)
    {
        var (_, state) = await RunDockerAsync("inspect", "--format", "{{json .State}}", containerName);
        var (_, logs) = await RunDockerAsync("logs", "--tail", "20", containerName);
        return $"docker State: {state.Trim()} | last log lines: {logs.Trim()}";
    }

    private static async Task<(int ExitCode, string Output)> RunDockerAsync(params string[] arguments)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException("docker did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }
}
