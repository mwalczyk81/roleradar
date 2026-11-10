using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using Xunit;

namespace RoleRadar.Tests.Integration.Containers;

/// <summary>
/// Real end-to-end integration test (T062) running production container images
/// (roleradar-identity, roleradar-api, roleradar-web) and an isolated MongoDB replica set
/// on an ephemeral throwaway Docker network with random ports, runtime secrets,
/// isolated SQLite Identity store, and direct audit log assertions.
/// Driven by Playwright in real Chromium.
/// </summary>
[Collection("BenchmarkCollection")]
[Trait("Lane", "real-stack")]
public sealed class RealStackEndToEndTests
{
    private const string DefaultIdentityImage = "roleradar-identity:local";
    private const string DefaultApiImage = "roleradar-api:local";
    private const string DefaultWebImage = "roleradar-web:local";

    [Fact]
    [Trait("Category", "Slow")]
    [Trait("Category", "RealStack")]
    public async Task RealStack_FullUserJourney_ExecutesUnderChromium_And_RecordsAuditLogs()
    {
        if (Environment.GetEnvironmentVariable("ROLERADAR_REAL_STACK_E2E") != "1")
        {
            Assert.Skip("Real-stack E2E test requires ROLERADAR_REAL_STACK_E2E=1 (run via scripts/test-e2e-real.ps1 or review-gate.ps1 -IncludeE2E).");
        }

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

        var identityImage = Environment.GetEnvironmentVariable("ROLERADAR_IDENTITY_IMAGE") ?? DefaultIdentityImage;
        var apiImage = Environment.GetEnvironmentVariable("ROLERADAR_API_IMAGE") ?? DefaultApiImage;
        var webImage = Environment.GetEnvironmentVariable("ROLERADAR_WEB_IMAGE") ?? DefaultWebImage;

        // Throwaway resource names with runId suffix
        var networkName = $"rrtest-real-net-{runId}";
        var mongoContainerName = $"rrtest-real-mongo-{runId}";
        var idContainerName = $"rrtest-real-id-{runId}";
        var apiContainerName = $"rrtest-real-api-{runId}";
        var webContainerName = $"rrtest-real-web-{runId}";

        var mongoVolumeName = $"rrtest-real-mvol-{runId}";
        var idVolumeName = $"rrtest-real-idvol-{runId}";

        var certTempDir = Path.Combine(Path.GetTempPath(), $"rrtest-real-certs-{runId}");
        Directory.CreateDirectory(certTempDir);

        var identityTempDir = Path.Combine(Path.GetTempPath(), $"rrtest-real-iddata-{runId}");
        Directory.CreateDirectory(identityTempDir);

        var pfxCertPath = Path.Combine(certTempDir, "cert.pfx");
        var pemCertPath = Path.Combine(certTempDir, "dev-cert.crt");
        var pemKeyPath = Path.Combine(certTempDir, "cert.key");

        // Runtime secrets
        var webClientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var headlessClientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var sessionEncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var certPassword = "RealPass!" + Guid.NewGuid().ToString("N")[..8];

        // Guardrails against touching production or developer resources
        Assert.NotEqual("roleradar-identity-data", idVolumeName);
        Assert.NotEqual("roleradar-mongo", mongoContainerName);

        const string ownerUsername = "throwaway-owner";
        const string ownerPassword = "StrongPassword2026E2E";

        try
        {
            // 1. Verify container images exist; build if necessary
            await EnsureImageExistsAsync(identityImage, "deploy/Dockerfile.identity", repoRoot);
            await EnsureImageExistsAsync(apiImage, "deploy/Dockerfile.api", repoRoot);
            await EnsureImageExistsAsync(webImage, "deploy/Dockerfile.web", repoRoot);

            // 2. Generate self-signed RSA SAN cert (identity, api, web, localhost, 127.0.0.1)
            GenerateSelfSignedCertificate(pfxCertPath, pemCertPath, pemKeyPath, certPassword);

            // 3. Create throwaway docker network
            var createNet = await RunDockerAsync($"network create {networkName}", repoRoot, 30);
            Assert.True(createNet.ExitCode == 0, $"Failed creating network {networkName}:\n{createNet.StdErr}");

            // 4. Run isolated MongoDB replica set container on throwaway network
            var runMongoCmd =
                $"run -d --name {mongoContainerName} --network {networkName} --network-alias mongo " +
                "-e GLIBC_TUNABLES=glibc.pthread.rseq=1 " +
                $"-v {mongoVolumeName}:/data/db " +
                "-p 127.0.0.1::27018 " +
                $"{MongoTestImage.Tag} mongod --replSet rs0 --port 27018 --bind_ip 127.0.0.1,mongo";

            var startMongo = await RunDockerAsync(runMongoCmd, repoRoot, 45);
            Assert.True(startMongo.ExitCode == 0, $"Failed starting mongo container:\n{startMongo.StdErr}");

            var mongoHostPort = await ResolveHostPortAsync(mongoContainerName, "27018/tcp", repoRoot);
            var hostMongoUri = $"mongodb://127.0.0.1:{mongoHostPort}/?replicaSet=rs0&directConnection=true";

            // Wait for MongoDB and initiate replica set
            await WaitForReplicaSetReadyAsync(hostMongoUri, mongoContainerName);

            // 5. Pre-seed Identity SQLite database on host before starting Identity container
            var identityDbPath = Path.Combine(identityTempDir, "identity.db");
            var sqliteConnStr = $"Data Source={identityDbPath}";

            var dbOptions = new DbContextOptionsBuilder<IdentityAppDbContext>()
                .UseSqlite(sqliteConnStr)
                .Options;

            Guid ownerSubId;
            string totpKey;

            await using (var db = new IdentityAppDbContext(dbOptions))
            {
                await db.Database.MigrateAsync();
                var prov = new OwnerProvisioningService(db);
                var provisionResult = await prov.ProvisionOwnerAsync(ownerUsername, ownerPassword);
                ownerSubId = provisionResult.UserId;
                totpKey = provisionResult.AuthenticatorKey;
                var initialTotp = prov.ComputeTotpCode(totpKey);
                var activated = await prov.VerifyTotpAndActivateAsync(ownerSubId, initialTotp);
                Assert.True(activated, "Failed to activate owner 2FA during SQLite seeding.");
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            var ownerSub = ownerSubId.ToString("D", System.Globalization.CultureInfo.InvariantCulture);

            // 6. Pre-seed MongoDB with fictional domain data (Companies, Postings, Criteria)
            var mongoClient = new MongoClient(hostMongoUri);
            var domainDb = mongoClient.GetDatabase("roleradar");

            await TrackingIndexInitializer.EnsureIndexesAsync(domainDb, CancellationToken.None);

            var companyRepo = new MongoCompanyRepository(mongoClient, domainDb);
            var postingRepo = new MongoPostingRepository(mongoClient, domainDb);

            var company1Id = Guid.NewGuid();
            var company1 = Company.Create(
                id: company1Id,
                ownerId: OwnerId.DefaultValue,
                name: "Acme Cloud Innovations",
                ownershipType: OwnershipType.Private,
                status: CompanyStatus.Target,
                description: "Cloud infrastructure and automation tools",
                headquarters: "Seattle, WA",
                categoryTags: ["cloud", "infra"]);

            var company2Id = Guid.NewGuid();
            var company2 = Company.Create(
                id: company2Id,
                ownerId: OwnerId.DefaultValue,
                name: "Beta Global Networks",
                ownershipType: OwnershipType.Public,
                status: CompanyStatus.Watch,
                description: "High-performance distributed systems",
                headquarters: "Austin, TX",
                categoryTags: ["networking", "distributed"]);

            var upsertComp1 = await companyRepo.UpsertCompanyAsync(company1);
            Assert.True(upsertComp1.IsSuccess && upsertComp1.Value);

            var upsertComp2 = await companyRepo.UpsertCompanyAsync(company2);
            Assert.True(upsertComp2.IsSuccess && upsertComp2.Value);

            var posting1Id = Guid.NewGuid();
            var posting1 = Posting.Create(
                id: posting1Id,
                ownerId: OwnerId.DefaultValue,
                companyId: company1Id,
                title: "Senior Cloud Architect",
                disposition: PostingDisposition.Lead,
                resurfacePolicy: ResurfacePolicy.Never,
                requisitionId: "REQ-CLD-101",
                location: "Remote / San Francisco, CA",
                score: 9.5m);

            var posting2Id = Guid.NewGuid();
            var posting2 = Posting.Create(
                id: posting2Id,
                ownerId: OwnerId.DefaultValue,
                companyId: company2Id,
                title: "Staff Platform Engineer",
                disposition: PostingDisposition.Lead,
                resurfacePolicy: ResurfacePolicy.Never,
                requisitionId: "REQ-PLT-202",
                location: "Remote",
                score: 8.8m);

            var upsertPost1 = await postingRepo.UpsertPostingAsync(posting1);
            Assert.True(upsertPost1.IsSuccess && upsertPost1.Value);

            var upsertPost2 = await postingRepo.UpsertPostingAsync(posting2);
            Assert.True(upsertPost2.IsSuccess && upsertPost2.Value);

            // 7. Find a free port for the Web container ahead of starting Identity
            var webHostPort = GetFreePort();

            // 8. Populate throwaway Docker volume for Identity SQLite database
            await RunDockerAsync($"volume create {idVolumeName}", repoRoot, 30);
            var idDataMount = identityTempDir.Replace('\\', '/');
            var copySeedCmd =
                $"run --rm -v {idVolumeName}:/data/identity -v \"{idDataMount}:/host:ro\" alpine:3.21.3 " +
                "sh -c \"cp -a /host/identity.db* /data/identity/ && chown -R 1654:1654 /data/identity && chmod 700 /data/identity && chmod 600 /data/identity/identity.db*\"";
            var copySeed = await RunDockerAsync(copySeedCmd, repoRoot, 30);
            Assert.True(copySeed.ExitCode == 0, $"Failed copying seed identity database to volume {idVolumeName}:\n{copySeed.StdErr}");

            // Start Identity container on throwaway network
            var idCertMount = pfxCertPath.Replace('\\', '/');

            var runIdCmd =
                $"run -d --name {idContainerName} --network {networkName} --network-alias identity " +
                "--read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m " +
                $"-v {idVolumeName}:/data/identity:rw " +
                $"-v \"{idCertMount}:/https/cert.pfx:ro\" " +
                "-e ROLERADAR_IDENTITY_ISSUER_URI=https://identity:5001 " +
                $"-e ROLERADAR_WEB_CLIENT_SECRET={webClientSecret} " +
                $"-e ROLERADAR_HEADLESS_CLIENT_SECRET={headlessClientSecret} " +
                $"-e Clients__Web__RedirectUri=https://127.0.0.1:{webHostPort}/auth/callback " +
                $"-e Clients__Web__PostLogoutRedirectUri=https://127.0.0.1:{webHostPort}/auth/logout " +
                "-e Kestrel__Endpoints__LoopbackHealth__Url=http://0.0.0.0:8080 " +
                "-e Kestrel__Endpoints__Https__Url=https://*:5001 " +
                "-e Kestrel__Endpoints__Https__Certificate__Path=/https/cert.pfx " +
                $"-e Kestrel__Endpoints__Https__Certificate__Password={certPassword} " +
                "-p 127.0.0.1::8080 -p 127.0.0.1::5001 " +
                "--cap-drop ALL --security-opt no-new-privileges " +
                identityImage;

            var startId = await RunDockerAsync(runIdCmd, repoRoot, 30);
            Assert.True(startId.ExitCode == 0, $"Failed starting Identity container:\n{startId.StdErr}");

            var idHealthPort = await ResolveHostPortAsync(idContainerName, "8080/tcp", repoRoot);
            var idHttpsPort = await ResolveHostPortAsync(idContainerName, "5001/tcp", repoRoot);
            await WaitForHttpEndpointAsync($"http://127.0.0.1:{idHealthPort}/health", "Identity", idContainerName, repoRoot);

            // 9. Start API container on throwaway network
            var apiPfxMount = pfxCertPath.Replace('\\', '/');
            var apiCrtMount = pemCertPath.Replace('\\', '/');

            var runApiCmd =
                $"run -d --name {apiContainerName} --network {networkName} --network-alias api " +
                "--read-only --tmpfs /tmp:rw,noexec,nosuid,size=64m " +
                $"-v \"{apiPfxMount}:/https/cert.pfx:ro\" " +
                $"-v \"{apiCrtMount}:/https/dev-cert.crt:ro\" " +
                "-e ROLERADAR_MONGO_CONNECTION_STRING=mongodb://mongo:27018/?directConnection=true " +
                "-e ROLERADAR_MONGO_DATABASE=roleradar " +
                "-e ROLERADAR_OIDC_AUTHORITY=https://identity:5001 " +
                "-e ROLERADAR_OIDC_AUDIENCE=roleradar.api " +
                $"-e Owner__AllowedSubjects__0={ownerSub} " +
                "-e SSL_CERT_FILE=/https/dev-cert.crt " +
                "-e Kestrel__Endpoints__LoopbackHealth__Url=http://0.0.0.0:8080 " +
                "-e Kestrel__Endpoints__Https__Url=https://*:5002 " +
                "-e Kestrel__Endpoints__Https__Certificate__Path=/https/cert.pfx " +
                $"-e Kestrel__Endpoints__Https__Certificate__Password={certPassword} " +
                "-p 127.0.0.1::8080 -p 127.0.0.1::5002 " +
                "--cap-drop ALL --security-opt no-new-privileges " +
                apiImage;

            var startApi = await RunDockerAsync(runApiCmd, repoRoot, 30);
            Assert.True(startApi.ExitCode == 0, $"Failed starting API container:\n{startApi.StdErr}");

            var apiHealthPort = await ResolveHostPortAsync(apiContainerName, "8080/tcp", repoRoot);
            var apiHttpsPort = await ResolveHostPortAsync(apiContainerName, "5002/tcp", repoRoot);
            await WaitForHttpEndpointAsync($"http://127.0.0.1:{apiHealthPort}/health", "API", apiContainerName, repoRoot);

            // 10. Start Web container on throwaway network bound to webHostPort
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
                $"-e ROLERADAR_WEB_CLIENT_SECRET={webClientSecret} " +
                $"-e SESSION_ENCRYPTION_KEY={sessionEncryptionKey} " +
                $"-e ROLERADAR_WEB_REDIRECT_URI=https://127.0.0.1:{webHostPort}/auth/callback " +
                $"-e ROLERADAR_WEB_POST_LOGOUT_URI=https://127.0.0.1:{webHostPort}/auth/logout " +
                "-e HTTPS_CERT_PATH=/https/cert.crt " +
                "-e HTTPS_CERT_KEY_PATH=/https/cert.key " +
                $"-p 127.0.0.1:{webHostPort}:5003 " +
                "--cap-drop ALL --security-opt no-new-privileges " +
                webImage;

            var startWeb = await RunDockerAsync(runWebCmd, repoRoot, 30);
            Assert.True(startWeb.ExitCode == 0, $"Failed starting Web container:\n{startWeb.StdErr}");

            // Wait for Web to respond on its HTTPS endpoint
            await WaitForHttpsWebEndpointAsync($"https://127.0.0.1:{webHostPort}/service-unavailable", pemCertPath, webContainerName, repoRoot);

            using var webProbeClient = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
                AllowAutoRedirect = false
            });
            var probeRes = await webProbeClient.GetAsync($"https://127.0.0.1:{webHostPort}/postings");
            var authLocation = probeRes.Headers.Location?.ToString() ?? "NO_LOCATION";

            // 11. Execute Playwright in real Chromium. The operability spec (feature 013) writes the correlation id of a
            // failing request to this file so the harness can follow it through the containers' logs below.
            var operabilityIdFile = Path.Combine(Path.GetTempPath(), $"rrtest-real-operability-{runId}.txt");
            var playwrightResult = await RunPlaywrightAsync(
                workingDirectory: repoRoot,
                baseUrl: $"https://127.0.0.1:{webHostPort}",
                identityPort: idHttpsPort,
                apiPort: apiHttpsPort,
                username: ownerUsername,
                password: ownerPassword,
                totpKey: totpKey,
                operabilityIdFile: operabilityIdFile);

            if (playwrightResult.ExitCode != 0)
            {
                var idLogs = await RunDockerAsync($"logs {idContainerName}", repoRoot, 15);
                var webLogs = await RunDockerAsync($"logs {webContainerName}", repoRoot, 15);
                var apiLogs = await RunDockerAsync($"logs {apiContainerName}", repoRoot, 15);

                var allIdLogs = idLogs.StdOut + "\n" + idLogs.StdErr;
                var idFiltered = string.Join("\n", allIdLogs.Split('\n').Where(l =>
                    l.Contains("Duende", StringComparison.OrdinalIgnoreCase) ||
                    l.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
                    l.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
                    l.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    l.Contains("authorize", StringComparison.OrdinalIgnoreCase) ||
                    l.Contains("login", StringComparison.OrdinalIgnoreCase) ||
                    l.Contains("redirect", StringComparison.OrdinalIgnoreCase)));

                Assert.Fail($"Playwright real-stack E2E test failed with exit code {playwrightResult.ExitCode}:\n" +
                    $"--- AUTH LOCATION ---\n{authLocation}\n" +
                    $"--- PLAYWRIGHT STDOUT ---\n{playwrightResult.StdOut}\n" +
                    $"--- PLAYWRIGHT STDERR ---\n{playwrightResult.StdErr}\n" +
                    $"--- FILTERED IDENTITY LOGS ---\n{idFiltered}\n" +
                    $"--- WEB LOGS ---\n{webLogs.StdOut}\n{webLogs.StdErr}\n" +
                    $"--- API LOGS ---\n{apiLogs.StdOut}\n{apiLogs.StdErr}\n");
            }

            Assert.True(
                playwrightResult.ExitCode == 0,
                $"Playwright real-stack E2E test failed with exit code {playwrightResult.ExitCode}:\n{playwrightResult.StdErr}\n{playwrightResult.StdOut}");

            // 11b. Feature 013 (SC-001, SC-002): the failing request's id appears on JSON lines from web and api, and
            // every line from the identity, web and api containers is one JSON object.
            Assert.True(File.Exists(operabilityIdFile), "The operability spec did not write the correlation id file.");
            var followedId = (await File.ReadAllTextAsync(operabilityIdFile)).Trim();
            try { File.Delete(operabilityIdFile); } catch { }
            Assert.Matches("^[A-Za-z0-9][A-Za-z0-9._-]{7,63}$", followedId);

            var containerLines = new Dictionary<string, List<string>>();
            foreach (var (service, container) in new[] { ("identity", idContainerName), ("web", webContainerName), ("api", apiContainerName) })
            {
                var logs = await RunDockerAsync($"logs {container}", repoRoot, 15);
                containerLines[service] = (logs.StdOut + "\n" + logs.StdErr)
                    .Split('\n')
                    .Select(l => l.TrimEnd('\r'))
                    .Where(l => l.Length > 0)
                    .ToList();
            }

            foreach (var (service, lines) in containerLines)
            {
                Assert.NotEmpty(lines);
                var notJson = lines.Where(l => !IsJsonObjectLine(l)).Take(5).ToList();
                Assert.True(notJson.Count == 0, $"{service} wrote non-JSON log lines:\n{string.Join("\n", notJson)}");
            }

            foreach (var service in new[] { "web", "api" })
            {
                Assert.True(
                    containerLines[service].Any(l => CorrelationIdOf(l) == followedId),
                    $"No {service} log line carries correlation id {followedId}.");
            }

            // 12. Assert directly against the isolated Mongo `audit_log`
            var auditCollection = domainDb.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
            var allAuditLogs = await auditCollection.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();

            // A. Posting disposition transition: exactly one entry, actor == the harness owner subject (feature 013)
            var transitionEntry = Assert.Single(allAuditLogs, d =>
                d.Contains("operation") && d["operation"].AsString == "posting_transition");
            Assert.Equal("posting", transitionEntry.GetValue("entity_type", "").AsString);
            Assert.Equal(ownerSub, transitionEntry.GetValue("actor", "").AsString);
            Assert.Equal(posting1Id, transitionEntry.GetValue("entity_id").AsGuid);

            // B. Application creation: exactly one entry, actor == the harness owner subject (feature 013)
            var appCreateEntry = Assert.Single(allAuditLogs, d =>
                d.Contains("operation") && d["operation"].AsString == "application_create");
            Assert.Equal("application", appCreateEntry.GetValue("entity_type", "").AsString);
            Assert.Equal(ownerSub, appCreateEntry.GetValue("actor", "").AsString);

            // C. Company update: exactly one entry, actor == the harness owner subject (feature 013)
            var compUpdateEntry = Assert.Single(allAuditLogs, d =>
                d.Contains("operation") && d["operation"].AsString == "company_update");
            Assert.Equal("company", compUpdateEntry.GetValue("entity_type", "").AsString);
            Assert.Equal(ownerSub, compUpdateEntry.GetValue("actor", "").AsString);
            Assert.Equal(company1Id, compUpdateEntry.GetValue("entity_id").AsGuid);
        }
        finally
        {
            // Full teardown in finally: containers, volumes, networks, temporary folders
            await TryRunDockerAsync($"rm -f {webContainerName}");
            await TryRunDockerAsync($"rm -f {apiContainerName}");
            await TryRunDockerAsync($"rm -f {idContainerName}");
            await TryRunDockerAsync($"rm -f {mongoContainerName}");

            await TryRunDockerAsync($"volume rm -f {mongoVolumeName}");
            await TryRunDockerAsync($"volume rm -f {idVolumeName}");
            await TryRunDockerAsync($"network rm {networkName}");

            try { Directory.Delete(certTempDir, recursive: true); } catch { }
            try { Directory.Delete(identityTempDir, recursive: true); } catch { }
        }
    }

    private static bool IsJsonObjectLine(string line)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(line);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static string? CorrelationIdOf(string line)
    {
        if (!IsJsonObjectLine(line))
        {
            return null;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(line);
        return doc.RootElement.TryGetProperty("correlationId", out var id) ? id.GetString() : null;
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunPlaywrightAsync(
        string workingDirectory,
        string baseUrl,
        int identityPort,
        int apiPort,
        string username,
        string password,
        string totpKey,
        string? operabilityIdFile = null,
        int timeoutSeconds = 120)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "pnpm",
            Arguments = isWindows
                ? "/c pnpm --filter web run test:e2e:real"
                : "--filter web run test:e2e:real",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.Environment["E2E_BASE_URL"] = baseUrl;
        psi.Environment["E2E_IDENTITY_PORT"] = identityPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        psi.Environment["E2E_API_PORT"] = apiPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        psi.Environment["E2E_USERNAME"] = username;
        psi.Environment["E2E_PASSWORD"] = password;
        psi.Environment["E2E_TOTP_KEY"] = totpKey;
        psi.Environment["NODE_TLS_REJECT_UNAUTHORIZED"] = "0";
        if (!string.IsNullOrEmpty(operabilityIdFile))
        {
            psi.Environment["E2E_OPERABILITY_ID_FILE"] = operabilityIdFile;
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start Playwright runner.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"Playwright test timed out after {timeoutSeconds}s.");
        }

        return (process.ExitCode, await stdout, await stderr);
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForReplicaSetReadyAsync(string hostMongoUri, string containerName)
    {
        var client = new MongoClient(hostMongoUri);
        var admin = client.GetDatabase("admin");

        var connected = false;
        for (var i = 0; i < 30; i++)
        {
            try
            {
                await admin.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
                connected = true;
                break;
            }
            catch
            {
                await Task.Delay(500);
            }
        }

        if (!connected)
        {
            throw new InvalidOperationException($"MongoDB container '{containerName}' failed to accept connections.");
        }

        try
        {
            await admin.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                ["replSetInitiate"] = new BsonDocument
                {
                    ["_id"] = "rs0",
                    ["members"] = new BsonArray
                    {
                        new BsonDocument { ["_id"] = 0, ["host"] = "127.0.0.1:27018" }
                    }
                }
            });
        }
        catch (MongoCommandException ex) when (ex.CodeName == "AlreadyInitialized")
        {
            // Replica set already initialized
        }
    }

    private static async Task WaitForHttpEndpointAsync(string url, string serviceName, string containerName, string repoRoot)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var res = await client.GetAsync(url);
                if (res.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Startup delay
            }

            await Task.Delay(500);
        }

        var logs = await RunDockerAsync($"logs {containerName}", repoRoot, 15);
        Assert.Fail($"{serviceName} failed to become healthy within 45s. Logs:\n{logs.StdOut}\n{logs.StdErr}");
    }

    private static async Task WaitForHttpsWebEndpointAsync(string url, string certPath, string containerName, string repoRoot)
    {
        using var x509Cert = X509CertificateLoader.LoadCertificateFromFile(certPath);
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
        using var client = new HttpClient(httpHandler) { Timeout = TimeSpan.FromSeconds(3) };

        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var res = await client.GetAsync(url);
                if (res.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Startup delay
            }

            await Task.Delay(500);
        }

        var logs = await RunDockerAsync($"logs {containerName}", repoRoot, 15);
        Assert.Fail($"Web container failed to respond on HTTPS within 45s. Logs:\n{logs.StdOut}\n{logs.StdErr}");
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
        san.AddDnsName("web");
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

        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        File.WriteAllBytes(pfxPath, pfxBytes);

        var pemCert = cert.ExportCertificatePem();
        File.WriteAllText(crtPath, pemCert);

        var pemKey = rsa.ExportPkcs8PrivateKeyPem();
        File.WriteAllText(keyPath, pemKey);
    }

    private static async Task EnsureImageExistsAsync(string imageTag, string dockerfilePath, string repoRoot)
    {
        var check = await RunDockerAsync($"image inspect {imageTag}", repoRoot, 15);
        if (check.ExitCode != 0)
        {
            var build = await RunDockerAsync($"build -f {dockerfilePath} -t {imageTag} .", repoRoot, 600);
            Assert.True(build.ExitCode == 0, $"Failed building image {imageTag}:\n{build.StdErr}\n{build.StdOut}");
        }
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
            // Ignore teardown errors
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
            try { process.Kill(entireProcessTree: true); } catch { }
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
