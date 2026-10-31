using System.Diagnostics;
using System.Net;
using System.Text;
using Xunit;

namespace RoleRadar.Tests.Integration.Containers;

/// <summary>
/// Regression guard: the Identity image once built without its compiled Razor login page because the
/// deny-by-default <c>.dockerignore</c> dropped <c>*.cshtml</c>. The container still reported healthy
/// (<c>/health</c> is a minimal endpoint) but <c>GET /Account/Login</c> returned 404, so nobody could sign in.
/// This test builds the real image, inspects the compiled assembly, runs the container, and fetches the page.
/// All Docker resources are uniquely named and throwaway; the developer's Mongo and the
/// <c>roleradar-identity-data</c> volume are never referenced.
/// </summary>
[Collection("BenchmarkCollection")]
public sealed class IdentityImageLoginPageTests
{
    // Separate tag so this test never overwrites the developer's roleradar-identity:local image.
    private const string ImageTag = "roleradar-identity:login-page-test";
    private const string RazorTypeMarker = "Pages_Account_Login";

    [Fact]
    [Trait("Category", "Slow")]
    public async Task IdentityImage_ContainsCompiledLoginPage_AndServesLoginFormOverHttp()
    {
        if (!await IsDockerAvailableAsync())
        {
            Assert.Skip("Docker CLI/daemon is unavailable.");
        }

        var repoRoot = FindRepoRoot();
        var runId = Guid.NewGuid().ToString("N")[..12];
        var containerName = $"rrtest-login-{runId}";
        var extractName = $"rrtest-login-extract-{runId}";
        var volumeName = $"rrtest-login-vol-{runId}";
        var extractedDll = Path.Combine(Path.GetTempPath(), $"rrtest-identity-{runId}.dll");

        // Hard guard: this test must only ever touch its own throwaway resources.
        Assert.NotEqual("roleradar-identity-data", volumeName);

        try
        {
            // 1. Build the real image (layer cache makes repeat runs cheap).
            var build = await RunDockerAsync(
                $"build -f deploy/Dockerfile.identity -t {ImageTag} .",
                repoRoot,
                timeoutSeconds: 600);
            Assert.True(build.ExitCode == 0, $"docker build failed:\n{build.StdErr}\n{build.StdOut}");

            // 2. The compiled assembly baked into the image must contain the Razor page type.
            //    Without *.cshtml in the build context the Razor SDK compiles nothing, so this fails first.
            var create = await RunDockerAsync($"create --name {extractName} {ImageTag}", repoRoot, 60);
            Assert.True(create.ExitCode == 0, $"docker create failed:\n{create.StdErr}");

            var copy = await RunDockerAsync($"cp {extractName}:/app/RoleRadar.Identity.dll \"{extractedDll}\"", repoRoot, 60);
            Assert.True(copy.ExitCode == 0, $"docker cp failed:\n{copy.StdErr}");

            var assemblyBytes = await File.ReadAllBytesAsync(extractedDll);
            Assert.True(
                assemblyBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(RazorTypeMarker)) >= 0,
                $"/app/RoleRadar.Identity.dll in the image does not contain the compiled Razor type '{RazorTypeMarker}'. " +
                "Check that .dockerignore allows src/**/*.cshtml.");

            // 3. Run the image with throwaway secrets and a throwaway data volume.
            //    ASPNETCORE_URLS is overridden to listen on all container interfaces so a loopback-published
            //    host port can reach it; the image default (127.0.0.1) is only reachable from inside the container.
            var run = await RunDockerAsync(
                $"run -d --name {containerName} --read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m " +
                $"-v {volumeName}:/data/identity:rw " +
                "-e ASPNETCORE_URLS=http://0.0.0.0:8080 " +
                "-e ROLERADAR_IDENTITY_DB=\"Data Source=/data/identity/identity.db\" " +
                "-e ROLERADAR_WEB_CLIENT_SECRET=\"LoginPageWebSecret!2026\" " +
                "-e ROLERADAR_HEADLESS_CLIENT_SECRET=\"LoginPageHeadlessSecret!2026\" " +
                "-p 127.0.0.1::8080 --cap-drop ALL --security-opt no-new-privileges " +
                ImageTag,
                repoRoot,
                30);
            Assert.True(run.ExitCode == 0, $"docker run failed:\n{run.StdErr}");

            var port = await ResolveHostPortAsync(containerName, repoRoot);

            // 4. GET /Account/Login must be 200 and contain the login form (no redirect, no 404).
            using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };

            HttpResponseMessage? response = null;
            string body = string.Empty;
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    response = await http.GetAsync("/Account/Login");
                    body = await response.Content.ReadAsStringAsync();
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        break;
                    }

                    // 404 is a definitive answer for the regression; only retry while the host is still starting.
                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                    // Host not accepting connections yet.
                }

                await Task.Delay(500);
            }

            if (response is null || response.StatusCode != HttpStatusCode.OK)
            {
                var logs = await RunDockerAsync($"logs {containerName}", repoRoot, 15);
                Assert.Fail(
                    $"GET /Account/Login returned {(response is null ? "no response" : ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture))}.\n" +
                    $"Container logs:\n{logs.StdOut}\n{logs.StdErr}");
            }

            Assert.Contains("<form", body, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("name=\"Input.Username\"", body, StringComparison.Ordinal);
            Assert.Contains("name=\"Input.Password\"", body, StringComparison.Ordinal);
            Assert.Contains("__RequestVerificationToken", body, StringComparison.Ordinal);
        }
        finally
        {
            await TryRunDockerAsync($"rm -f {containerName}");
            await TryRunDockerAsync($"rm -f {extractName}");
            await TryRunDockerAsync($"volume rm -f {volumeName}");
            try
            {
                if (File.Exists(extractedDll))
                {
                    File.Delete(extractedDll);
                }
            }
            catch (IOException)
            {
                // Best-effort temp cleanup.
            }
        }
    }

    private static async Task<int> ResolveHostPortAsync(string containerName, string workingDirectory)
    {
        // `docker port` prints e.g. "127.0.0.1:49153"; retry briefly because the mapping appears right after start.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var result = await RunDockerAsync($"port {containerName} 8080/tcp", workingDirectory, 10);
            var line = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (result.ExitCode == 0 && line is not null && int.TryParse(line[(line.LastIndexOf(':') + 1)..], out var port))
            {
                return port;
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException($"Could not resolve the published host port for container '{containerName}'.");
    }

    private static async Task<bool> IsDockerAvailableAsync()
    {
        try
        {
            var result = await RunDockerAsync("info --format \"{{.ServerVersion}}\"", Environment.CurrentDirectory, 15);
            return result.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task TryRunDockerAsync(string arguments)
    {
        try
        {
            await RunDockerAsync(arguments, Environment.CurrentDirectory, 30);
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or OperationCanceledException)
        {
            // Cleanup must never mask the real test result.
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunDockerAsync(
        string arguments,
        string workingDirectory,
        int timeoutSeconds)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start docker {arguments}");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"docker {arguments} timed out after {timeoutSeconds}s.");
        }

        return (process.ExitCode, await stdout, await stderr);
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
