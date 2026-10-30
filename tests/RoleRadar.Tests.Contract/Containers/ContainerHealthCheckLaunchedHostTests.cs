using System.Diagnostics;
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

        var containerName = $"rrtest-hc-{serviceName}-{Guid.NewGuid():N}";
        try
        {
            // Launch under supported read-only rootfs mode with tmpfs /tmp (and tmpfs /data/identity for SQLite in identity container)
            var extraTmpfs = serviceName == "identity"
                ? "--tmpfs /data/identity:rw,noexec,nosuid,size=64m -e ROLERADAR_IDENTITY_DB=\"Data Source=/data/identity/identity.db\" "
                : "-e ROLERADAR_MONGO_CONNECTION_STRING=\"mongodb://127.0.0.1:27099/?directConnection=true\" -e ROLERADAR_MONGO_DATABASE=\"rrtest_hc\" ";

            var runResult = await RunProcessAsync(
                "docker",
                $"run -d --name {containerName} --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m {extraTmpfs} --cap-drop ALL --security-opt no-new-privileges {imageTag}",
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
