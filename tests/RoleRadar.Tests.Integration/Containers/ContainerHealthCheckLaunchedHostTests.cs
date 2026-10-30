using System.Diagnostics;
using Xunit;

namespace RoleRadar.Tests.Integration.Containers;

[Collection("BenchmarkCollection")]
public sealed class ContainerHealthCheckLaunchedHostTests
{
    [Theory]
    [InlineData("identity", "deploy/Dockerfile.identity", "roleradar-identity:test")]
    [InlineData("api", "deploy/Dockerfile.api", "roleradar-api:test")]
    public async Task ContainerImage_UnderReadOnlyRootfs_ReportsHealthyInDockerInspect(
        string serviceName,
        string dockerfileRelativePath,
        string imageTag)
    {
        var repoRoot = FindRepoRoot();
        var dockerfilePath = Path.Combine(repoRoot, dockerfileRelativePath);
        Assert.True(File.Exists(dockerfilePath), $"Expected Dockerfile at '{dockerfilePath}'.");

        // Build container image
        var buildResult = await RunProcessAsync(
            "docker",
            $"build -f \"{dockerfilePath}\" -t {imageTag} .",
            repoRoot,
            timeoutSeconds: 300);
        Assert.True(
            buildResult.ExitCode == 0,
            $"docker build failed for {imageTag}:\n{buildResult.StandardError}\n{buildResult.StandardOutput}");

        var runGuid = Guid.NewGuid().ToString("N");
        var containerName = $"rrtest-hc-{serviceName}-{runGuid}";
        var identityVolumeName = $"rrtest-hc-identity-vol-{runGuid}";
        try
        {
            // Launch under supported read-only rootfs mode with tmpfs /tmp.
            // For identity, mount a fresh named volume at /data/identity to verify that /data/identity
            // in the image is owned by 1654:1654 so the named volume inherits ownership for SQLite,
            // Data Protection keys, and Duende KeyManagement keys.
            var extraArgs = serviceName == "identity"
                ? $"-v {identityVolumeName}:/data/identity:rw -e ROLERADAR_IDENTITY_DB=\"Data Source=/data/identity/identity.db\" "
                : "-e ROLERADAR_MONGO_CONNECTION_STRING=\"mongodb://127.0.0.1:27099/?directConnection=true\" -e ROLERADAR_MONGO_DATABASE=\"rrtest_hc\" ";

            var runResult = await RunProcessAsync(
                "docker",
                $"run -d --name {containerName} --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m {extraArgs} --cap-drop ALL --security-opt no-new-privileges {imageTag}",
                repoRoot,
                timeoutSeconds: 30);

            Assert.True(
                runResult.ExitCode == 0,
                $"docker run failed for {containerName}:\n{runResult.StandardError}");

            // Poll docker inspect until State.Health.Status == "healthy" (within 20s)
            var status = "starting";
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(20))
            {
                var inspectResult = await RunProcessAsync(
                    "docker",
                    $"inspect --format \"{{{{.State.Health.Status}}}}\" {containerName}",
                    repoRoot,
                    timeoutSeconds: 10);

                status = inspectResult.StandardOutput.Trim();
                if (string.Equals(status, "healthy", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await Task.Delay(500);
            }

            if (!string.Equals(status, "healthy", StringComparison.OrdinalIgnoreCase))
            {
                var logs = await RunProcessAsync("docker", $"logs {containerName}", repoRoot, 10);
                Assert.Fail($"Container '{containerName}' did not reach 'healthy' status (last status: '{status}'). Logs:\n{logs.StandardOutput}\n{logs.StandardError}");
            }

            Assert.Equal("healthy", status);
        }
        finally
        {
            try
            {
                await RunProcessAsync("docker", $"rm -f {containerName}", repoRoot, timeoutSeconds: 15);
                if (serviceName == "identity")
                {
                    await RunProcessAsync("docker", $"volume rm -f {identityVolumeName}", repoRoot, timeoutSeconds: 15);
                }
            }
            catch
            {
                // Ignore cleanup error
            }
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        int timeoutSeconds)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName} {arguments}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        await process.WaitForExitAsync(cts.Token);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate RoleRadar.sln from test base directory.");
    }
}
