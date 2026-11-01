using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RoleRadar.Tests.Integration.Containers;

/// <summary>
/// Container smoke test for the containerized web UI image (deploy/Dockerfile.web).
/// Exercises the web image under production hardening conditions matching deploy/docker-compose.apps.yml:
/// read-only rootfs, non-root user (node), tmpfs /tmp, cap_drop ALL, no-new-privileges, in-process TLS on 5003.
/// Verifies OIDC discovery integration with an ephemeral Identity container, health/CSP headers,
/// unauthenticated /postings redirection to Identity with PKCE HttpOnly cookies, inspect security flags,
/// log hygiene (no raw secret leakage), and fail-closed termination on missing secrets or TLS assets.
/// </summary>
[Collection("BenchmarkCollection")]
public sealed class WebContainerSmokeTests
{
    private const string DefaultIdentityImageTag = "roleradar-identity:test";
    private const string DefaultWebImageTag = "roleradar-web:test";

    [Fact]
    [Trait("Category", "Slow")]
    public async Task WebContainer_UnderReadOnlyRootfsAndTls_PassesSmokeAndSecurityChecks()
    {
        if (!await IsDockerAvailableAsync())
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
            {
                Assert.Fail("Docker CLI/daemon is unavailable in CI environment.");
            }

            Assert.Skip("Docker CLI/daemon is unavailable.");
        }

        var repoRoot = FindRepoRoot();
        var runId = Guid.NewGuid().ToString("N")[..12];

        var identityImage = Environment.GetEnvironmentVariable("ROLERADAR_IDENTITY_IMAGE");
        if (string.IsNullOrWhiteSpace(identityImage))
        {
            identityImage = DefaultIdentityImageTag;
        }

        var webImage = Environment.GetEnvironmentVariable("ROLERADAR_WEB_IMAGE");
        if (string.IsNullOrWhiteSpace(webImage))
        {
            webImage = DefaultWebImageTag;
        }

        // Ephemeral resource identifiers with runId suffix
        var networkName = $"rrtest-webnet-{runId}";
        var identityContainerName = $"rrtest-web-id-{runId}";
        var webContainerName = $"rrtest-web-ui-{runId}";
        var identityVolumeName = $"rrtest-webvol-id-{runId}";

        var certTempDir = Path.Combine(Path.GetTempPath(), $"rrtest-web-certs-{runId}");
        Directory.CreateDirectory(certTempDir);

        var pfxCertPath = Path.Combine(certTempDir, "cert.pfx");
        var pemCertPath = Path.Combine(certTempDir, "dev-cert.crt");
        var pemKeyPath = Path.Combine(certTempDir, "dev-cert.key");

        // Dynamic runtime secrets (never literal in repo)
        var webClientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var headlessClientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var sessionEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var certPassword = "WebCertPass!" + Guid.NewGuid().ToString("N")[..12];
        var ownerSub = Guid.NewGuid().ToString("D");

        // Guardrails
        Assert.NotEqual("roleradar-identity-data", identityVolumeName);

        try
        {
            // 1. Build Identity and Web images if needed
            var checkIdImg = await RunDockerAsync($"image inspect {identityImage}", repoRoot, 15);
            if (checkIdImg.ExitCode != 0)
            {
                var buildId = await RunDockerAsync(
                    $"build -f deploy/Dockerfile.identity -t {identityImage} .",
                    repoRoot,
                    timeoutSeconds: 600);
                Assert.True(buildId.ExitCode == 0, $"Failed building identity image:\n{buildId.StdErr}\n{buildId.StdOut}");
            }

            var checkWebImg = await RunDockerAsync($"image inspect {webImage}", repoRoot, 15);
            if (checkWebImg.ExitCode != 0)
            {
                var buildWeb = await RunDockerAsync(
                    $"build -f deploy/Dockerfile.web -t {webImage} .",
                    repoRoot,
                    timeoutSeconds: 600);
                Assert.True(buildWeb.ExitCode == 0, $"Failed building web image:\n{buildWeb.StdErr}\n{buildWeb.StdOut}");
            }

            // 2. Generate self-signed RSA cert with SANs (identity, api, localhost, 127.0.0.1)
            GenerateSelfSignedCertificate(pfxCertPath, pemCertPath, pemKeyPath, certPassword);

            // 3. Create throwaway docker network
            var createNet = await RunDockerAsync($"network create {networkName}", repoRoot, 30);
            Assert.True(createNet.ExitCode == 0, $"Failed creating network {networkName}:\n{createNet.StdErr}");

            // 4. Run Identity container on throwaway network with alias 'identity'
            //    Identity listens on 5001 for HTTPS and 8080 for health check.
            var idCertMount = pfxCertPath.Replace('\\', '/');
            var runIdCmd =
                $"run -d --name {identityContainerName} --network {networkName} --network-alias identity " +
                "--read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m " +
                $"-v {identityVolumeName}:/data/identity:rw " +
                $"-v \"{idCertMount}:/https/cert.pfx:ro\" " +
                "-e ROLERADAR_IDENTITY_DB=\"Data Source=/data/identity/identity.db\" " +
                "-e ROLERADAR_IDENTITY_ISSUER_URI=\"https://identity:5001\" " +
                $"-e ROLERADAR_WEB_CLIENT_SECRET=\"{webClientSecret}\" " +
                $"-e ROLERADAR_HEADLESS_CLIENT_SECRET=\"{headlessClientSecret}\" " +
                $"-e Clients__Web__RedirectUri=\"https://localhost:5003/auth/callback\" " +
                $"-e Clients__Web__PostLogoutRedirectUri=\"https://localhost:5003/auth/logout\" " +
                "-e Kestrel__Endpoints__LoopbackHealth__Url=\"http://0.0.0.0:8080\" " +
                "-e Kestrel__Endpoints__Https__Url=\"https://*:5001\" " +
                "-e Kestrel__Endpoints__Https__Certificate__Path=\"/https/cert.pfx\" " +
                $"-e Kestrel__Endpoints__Https__Certificate__Password=\"{certPassword}\" " +
                "-p 127.0.0.1::8080 -p 127.0.0.1::5001 " +
                "--cap-drop ALL --security-opt no-new-privileges " +
                identityImage;

            var startIdentity = await RunDockerAsync(runIdCmd, repoRoot, 30);
            Assert.True(startIdentity.ExitCode == 0, $"docker run failed for identity container:\n{startIdentity.StdErr}");

            var idHttpPort = await ResolveHostPortAsync(identityContainerName, "8080/tcp", repoRoot);

            // Wait for Identity to become healthy on its loopback /health endpoint
            using var idHttpClient = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{idHttpPort}") };
            var idHealthy = false;
            var deadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    var res = await idHttpClient.GetAsync("/health");
                    if (res.StatusCode == HttpStatusCode.OK)
                    {
                        idHealthy = true;
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                    // Startup delay
                }

                await Task.Delay(500);
            }

            if (!idHealthy)
            {
                var idLogs = await RunDockerAsync($"logs {identityContainerName}", repoRoot, 15);
                Assert.Fail($"Identity container failed to reach healthy state within 45s. Logs:\n{idLogs.StdOut}\n{idLogs.StdErr}");
            }

            // 5. Run Web container on the same network with compose-equivalent hardening flags
            var webCrtMount = pemCertPath.Replace('\\', '/');
            var webKeyMount = pemKeyPath.Replace('\\', '/');

            var runWebCmd =
                $"run -d --name {webContainerName} --network {networkName} " +
                "--read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m " +
                $"-v \"{webCrtMount}:/https/dev-cert.crt:ro\" " +
                $"-v \"{webCrtMount}:/https/cert.crt:ro\" " +
                $"-v \"{webKeyMount}:/https/cert.key:ro\" " +
                "-e NODE_ENV=production " +
                "-e NODE_EXTRA_CA_CERTS=/https/dev-cert.crt " +
                "-e ROLERADAR_IDENTITY_ISSUER_URI=https://identity:5001 " +
                "-e ROLERADAR_API_BASE_URL=https://api:5002 " +
                $"-e ROLERADAR_WEB_CLIENT_SECRET=\"{webClientSecret}\" " +
                $"-e SESSION_ENCRYPTION_KEY=\"{sessionEncryptionKey}\" " +
                "-e HTTPS_CERT_PATH=/https/cert.crt " +
                "-e HTTPS_CERT_KEY_PATH=/https/cert.key " +
                "-p 127.0.0.1::5003 " +
                "--cap-drop ALL --security-opt no-new-privileges " +
                webImage;

            var startWeb = await RunDockerAsync(runWebCmd, repoRoot, 30);
            Assert.True(startWeb.ExitCode == 0, $"docker run failed for web container:\n{startWeb.StdErr}");

            var webPort = await ResolveHostPortAsync(webContainerName, "5003/tcp", repoRoot);

            // Handler configured with trusting the generated self-signed test cert
            using var x509Cert = X509CertificateLoader.LoadCertificateFromFile(pemCertPath);
            using var httpHandler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, cert, chain, errors) =>
                    {
                        if (cert is not null && cert.GetCertHashString() == x509Cert.GetCertHashString())
                        {
                            return true;
                        }

                        return errors == SslPolicyErrors.None;
                    }
                }
            };
            using var webClient = new HttpClient(httpHandler) { BaseAddress = new Uri($"https://127.0.0.1:{webPort}") };

            // Wait for Web to listen and respond on 5003
            HttpResponseMessage? serviceUnavailableRes = null;
            var webDeadline = DateTime.UtcNow.AddSeconds(45);
            while (DateTime.UtcNow < webDeadline)
            {
                try
                {
                    serviceUnavailableRes = await webClient.GetAsync("/service-unavailable");
                    if (serviceUnavailableRes.StatusCode == HttpStatusCode.OK)
                    {
                        break;
                    }
                }
                catch (HttpRequestException)
                {
                    // Startup delay
                }

                await Task.Delay(500);
            }

            Assert.NotNull(serviceUnavailableRes);
            Assert.Equal(HttpStatusCode.OK, serviceUnavailableRes.StatusCode);

            // Assertion: GET /service-unavailable returns 200 with Content-Security-Policy header containing a nonce
            Assert.True(
                serviceUnavailableRes.Headers.TryGetValues("Content-Security-Policy", out var cspValues),
                "Missing Content-Security-Policy header on /service-unavailable response.");
            var cspHeader = string.Join(";", cspValues);
            Assert.Contains("'nonce-", cspHeader, StringComparison.OrdinalIgnoreCase);

            // Assertion: GET /postings without a session returns 302 to https://identity:5001/connect/authorize
            var postingsRes = await webClient.GetAsync("/postings");
            Assert.Equal(HttpStatusCode.Redirect, postingsRes.StatusCode);

            var location = postingsRes.Headers.Location?.ToString();
            Assert.NotNull(location);
            Assert.StartsWith("https://identity:5001/connect/authorize", location, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("client_id=roleradar-web", location, StringComparison.Ordinal);
            Assert.Contains("code_challenge_method=S256", location, StringComparison.Ordinal);
            Assert.Contains("state=", location, StringComparison.Ordinal);

            // Assertion: PKCE and state cookies set with HttpOnly
            Assert.True(postingsRes.Headers.TryGetValues("Set-Cookie", out var setCookies), "Expected Set-Cookie headers on redirect.");
            var cookiesList = setCookies.ToList();
            Assert.Contains(cookiesList, c => c.Contains("__Host-roleradar_auth_state") && c.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(cookiesList, c => c.Contains("__Host-roleradar_code_verifier") && c.Contains("HttpOnly", StringComparison.OrdinalIgnoreCase));

            // Assertion: GET /favicon.ico is not a 500
            var faviconRes = await webClient.GetAsync("/favicon.ico");
            Assert.NotEqual(HttpStatusCode.InternalServerError, faviconRes.StatusCode);

            // Assertion: docker inspect shows non-root user, ReadonlyRootfs true, CapDrop ALL
            var inspectRes = await RunDockerAsync($"inspect {webContainerName}", repoRoot, 15);
            Assert.Equal(0, inspectRes.ExitCode);
            using var doc = JsonDocument.Parse(inspectRes.StdOut);
            var hostConfig = doc.RootElement[0].GetProperty("HostConfig");
            var config = doc.RootElement[0].GetProperty("Config");

            var readonlyRootfs = hostConfig.GetProperty("ReadonlyRootfs").GetBoolean();
            Assert.True(readonlyRootfs, "Expected HostConfig.ReadonlyRootfs == true");

            var capDrop = hostConfig.GetProperty("CapDrop").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.Contains("ALL", capDrop);

            var user = config.GetProperty("User").GetString();
            Assert.True(!string.IsNullOrEmpty(user) && user != "0" && user != "root", $"Expected non-root User in config, got '{user}'");

            // Assertion: docker logs contains zero secret values
            var logsRes = await RunDockerAsync($"logs {webContainerName}", repoRoot, 15);
            var allLogs = logsRes.StdOut + "\n" + logsRes.StdErr;
            Assert.DoesNotContain(webClientSecret, allLogs, StringComparison.Ordinal);
            Assert.DoesNotContain(sessionEncryptionKey, allLogs, StringComparison.Ordinal);
            Assert.DoesNotContain(headlessClientSecret, allLogs, StringComparison.Ordinal);
            Assert.DoesNotContain(certPassword, allLogs, StringComparison.Ordinal);

            // Negative Test 1: missing SESSION_ENCRYPTION_KEY fails closed with non-zero exit
            var negNoKeyName = $"rrtest-web-nokey-{runId}";
            try
            {
                var runNoKey = await RunDockerAsync(
                    $"run --name {negNoKeyName} " +
                    "--read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m " +
                    $"-v \"{webCrtMount}:/https/dev-cert.crt:ro\" " +
                    $"-v \"{webCrtMount}:/https/cert.crt:ro\" " +
                    $"-v \"{webKeyMount}:/https/cert.key:ro\" " +
                    "-e NODE_ENV=production " +
                    "-e ROLERADAR_IDENTITY_ISSUER_URI=https://identity:5001 " +
                    $"-e ROLERADAR_WEB_CLIENT_SECRET=\"{webClientSecret}\" " +
                    "-e HTTPS_CERT_PATH=/https/cert.crt " +
                    "-e HTTPS_CERT_KEY_PATH=/https/cert.key " +
                    "--cap-drop ALL --security-opt no-new-privileges " +
                    webImage,
                    repoRoot,
                    30);

                Assert.NotEqual(0, runNoKey.ExitCode);
                var negLogs = runNoKey.StdOut + "\n" + runNoKey.StdErr;
                Assert.Contains("SESSION_ENCRYPTION_KEY", negLogs, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                await TryRunDockerAsync($"rm -f {negNoKeyName}");
            }

            // Negative Test 2: missing cert files fails closed with non-zero exit
            var negNoCertName = $"rrtest-web-nocert-{runId}";
            try
            {
                var runNoCert = await RunDockerAsync(
                    $"run --name {negNoCertName} " +
                    "--read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m " +
                    "-e NODE_ENV=production " +
                    "-e ROLERADAR_IDENTITY_ISSUER_URI=https://identity:5001 " +
                    $"-e ROLERADAR_WEB_CLIENT_SECRET=\"{webClientSecret}\" " +
                    $"-e SESSION_ENCRYPTION_KEY=\"{sessionEncryptionKey}\" " +
                    "-e HTTPS_CERT_PATH=/https/nonexistent.crt " +
                    "-e HTTPS_CERT_KEY_PATH=/https/nonexistent.key " +
                    "--cap-drop ALL --security-opt no-new-privileges " +
                    webImage,
                    repoRoot,
                    30);

                Assert.NotEqual(0, runNoCert.ExitCode);
                var negCertLogs = runNoCert.StdOut + "\n" + runNoCert.StdErr;
                Assert.Contains("certificate", negCertLogs, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                await TryRunDockerAsync($"rm -f {negNoCertName}");
            }
        }
        finally
        {
            // Defensive cleanup of all throwaway resources
            await TryRunDockerAsync($"rm -f {webContainerName}");
            await TryRunDockerAsync($"rm -f {identityContainerName}");
            await TryRunDockerAsync($"volume rm -f {identityVolumeName}");
            await TryRunDockerAsync($"network rm {networkName}");

            try
            {
                if (Directory.Exists(certTempDir))
                {
                    Directory.Delete(certTempDir, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort temp dir cleanup
            }
        }
    }

    private static void GenerateSelfSignedCertificate(string pfxPath, string crtPath, string keyPath, string password)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(
            "CN=identity",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("identity");
        san.AddDnsName("api");
        san.AddDnsName("localhost");
        san.AddIpAddress(IPAddress.Loopback);

        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(365));

        // Export PFX for Identity Kestrel
        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(pfxPath, pfxBytes);

        // Export PEM Certificate and PEM Private Key for Node.js Web Server
        var pemCert = cert.ExportCertificatePem();
        File.WriteAllText(crtPath, pemCert);

        var pemKey = rsa.ExportPkcs8PrivateKeyPem();
        File.WriteAllText(keyPath, pemKey);
    }

    private static async Task<int> ResolveHostPortAsync(string containerName, string containerPortSpec, string workingDirectory)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            var result = await RunDockerAsync($"port {containerName} {containerPortSpec}", workingDirectory, 10);
            var line = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (result.ExitCode == 0 && line is not null && int.TryParse(line[(line.LastIndexOf(':') + 1)..], out var port))
            {
                return port;
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException($"Could not resolve published host port for '{containerName}' on port '{containerPortSpec}'.");
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
            // Cleanup must never mask real test result
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
