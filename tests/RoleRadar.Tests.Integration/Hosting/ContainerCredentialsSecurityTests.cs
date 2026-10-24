using System.Diagnostics;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

/// <summary>
/// T019 / FR-003, FR-006, FR-011, SC-007: credential handling of the bundled Database Tools inside the
/// CLI image, observed from outside the process under the exact supported run mode
/// (<c>--read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges</c>).
///
/// How the observation works:
///  * A "holder" CLI container runs as PID 1 (a backup against a blackholed address, so it stays alive).
///    Keeping PID 1 alive keeps the container's tmpfs <c>/tmp</c> alive, so cleanup can be checked
///    AFTER a second CLI process exits. If the process under test were PID 1, its exit would destroy the
///    tmpfs and "no file remains" would be vacuously true.
///  * The process under test is started with <c>docker exec</c> (test tooling only; the product never
///    touches a Docker socket).
///  * A sidecar (<c>ubuntu:24.04</c>, same UID 1654, same hardening flags, <c>--network none</c>) joins the
///    holder's PID namespace and reads <c>/proc/&lt;pid&gt;/cmdline</c>, <c>/proc/&lt;pid&gt;/environ</c> and
///    <c>/proc/1/root/tmp</c> WHILE the child tool is running, and delivers SIGTERM.
///  * The database URI carries a real credential: a per-test MongoDB user (created on the compose
///    server; auth is not enforced there, but the driver and tools still authenticate with SCRAM, so the
///    credential is genuinely used). It is delivered through a read-only mounted file
///    (<c>ROLERADAR_MONGO_CONNECTION_STRING_FILE</c>), never through <c>-e</c>, so it cannot reach
///    <c>docker inspect</c>. The archive passphrase is delivered on stdin for the same reason.
/// </summary>
public sealed class ContainerCredentialsSecurityTests
{
    private const string HostMongoUri = "mongodb://127.0.0.1:27017/?replicaSet=rs0&directConnection=true";
    private const string SidecarImage = "ubuntu:24.04@sha256:68ee22004a9109b44973313b02ff7b1de250f1de0f5ecb628d73f579aaff33ac";

    private static readonly string[] HardeningFlags =
    [
        "--read-only", "--tmpfs", "/tmp:rw,noexec,nosuid,size=64m", "--cap-drop", "ALL", "--security-opt", "no-new-privileges"
    ];

    // ------------------------------------------------------------------------------------------------
    // Backup
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Backup_ChildArgvEnvironAndConfigMode_AreSafe_AndSigtermToCliLeavesNoConfig()
    {
        await using var s = await Scenario.StartAsync();

        await using var cli = s.ExecCli(["backup", "/tmp/sigterm.rrbak", "--passphrase-stdin"], "hang_uri");
        var probe = await s.ProbeToolChildAsync("mongodump");

        s.AssertChildIsSafe(probe);

        await s.SignalAsync(probe.ParentPid);
        var exit = await cli.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.NotEqual(0, exit.ExitCode);
        await s.AssertProcessGoneAsync(probe.ChildPid);
        await s.AssertOnlyHolderConfigRemainsAsync();
        s.AssertNoSecret(exit.StandardOutput, "exec stdout");
        s.AssertNoSecret(exit.StandardError, "exec stderr");
    }

    [Fact]
    public async Task Backup_ChildKilledBySigterm_LeavesNoConfig()
    {
        await using var s = await Scenario.StartAsync();

        await using var cli = s.ExecCli(["backup", "/tmp/childterm.rrbak", "--passphrase-stdin"], "hang_uri");
        var probe = await s.ProbeToolChildAsync("mongodump");

        await s.SignalAsync(probe.ChildPid);
        var exit = await cli.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.NotEqual(0, exit.ExitCode);
        await s.AssertOnlyHolderConfigRemainsAsync();
        s.AssertNoSecret(exit.StandardOutput, "exec stdout");
        s.AssertNoSecret(exit.StandardError, "exec stderr");
    }

    [Fact]
    public async Task Backup_ChildFailure_LeavesNoConfig_AndRedactsOutput()
    {
        await using var s = await Scenario.StartAsync();

        // fail_uri points at the blackholed address with a 2 s server selection timeout: mongodump exits non-zero on its own.
        await using var cli = s.ExecCli(["backup", "/tmp/fail.rrbak", "--passphrase-stdin"], "fail_uri");
        var exit = await cli.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotEqual(0, exit.ExitCode);
        await s.AssertOnlyHolderConfigRemainsAsync();
        s.AssertNoSecret(exit.StandardOutput, "exec stdout");
        s.AssertNoSecret(exit.StandardError, "exec stderr");
    }

    // ------------------------------------------------------------------------------------------------
    // Credentialed round trip: backup -> wipe -> restore against compose MongoDB
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task CredentialedRoundTrip_LeavesNoSecretInOutputsAuditFilesInspectOrHistory()
    {
        await using var s = await Scenario.StartAsync();
        await s.SeedAsync();

        var backup = await RunToEnd(s.ExecCli(["backup", "/data/backup/ok.rrbak", "--passphrase-stdin"], "good_uri"));
        Assert.True(backup.ExitCode == 0, $"backup failed: {backup.StandardError}");
        await s.AssertOnlyHolderConfigRemainsAsync();

        var wipe = await RunToEnd(s.ExecCli(["wipe", "--yes"], "good_uri", sendPassphrase: false));
        Assert.True(wipe.ExitCode == 0, $"wipe failed: {wipe.StandardError}");

        var restore = await RunToEnd(s.ExecCli(["restore", "/data/backup/ok.rrbak", "--passphrase-stdin"], "good_uri"));
        Assert.True(restore.ExitCode == 0, $"restore failed: {restore.StandardError}");
        await s.AssertOnlyHolderConfigRemainsAsync();

        foreach (var r in new[] { backup, wipe, restore })
        {
            s.AssertNoSecret(r.StandardOutput, "exec stdout");
            s.AssertNoSecret(r.StandardError, "exec stderr");
        }

        // Every document in the test database, including audit_log entries written by wipe/restore.
        await s.AssertDatabaseHasNoSecretAsync();

        // Every file written to the only writable mount, after the processes exited.
        s.AssertBackupDirectoryHasNoSecret();

        // Container metadata and image history.
        var inspect = await Docker(["inspect", s.Name]);
        s.AssertNoSecret(inspect.StandardOutput, "docker inspect (container)");
        var history = await Docker(["history", "--no-trunc", ContainerTestHarness.ResolveCliImage()]);
        s.AssertNoSecret(history.StandardOutput, "docker history");
        var imageInspect = await Docker(["image", "inspect", ContainerTestHarness.ResolveCliImage()]);
        s.AssertNoSecret(imageInspect.StandardOutput, "docker image inspect");
    }

    // ------------------------------------------------------------------------------------------------
    // Restore
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Restore_ChildArgvEnvironAndConfigMode_AreSafe_AndSigtermToCliLeavesNoConfig()
    {
        await using var s = await Scenario.StartAsync();
        await s.SeedAsync();
        var backup = await RunToEnd(s.ExecCli(["backup", "/data/backup/r.rrbak", "--passphrase-stdin"], "good_uri"));
        Assert.True(backup.ExitCode == 0, $"backup failed: {backup.StandardError}");

        // --force-overwrite skips the driver-side emptiness check, so mongorestore is spawned and then
        // blocks on server selection against the blackholed address.
        await using var cli = s.ExecCli(["restore", "/data/backup/r.rrbak", "--passphrase-stdin", "--force-overwrite"], "hang_uri");
        var probe = await s.ProbeToolChildAsync("mongorestore");

        s.AssertChildIsSafe(probe);

        await s.SignalAsync(probe.ParentPid);
        var exit = await cli.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.NotEqual(0, exit.ExitCode);
        await s.AssertProcessGoneAsync(probe.ChildPid);
        await s.AssertOnlyHolderConfigRemainsAsync();
        s.AssertNoSecret(exit.StandardOutput, "exec stdout");
        s.AssertNoSecret(exit.StandardError, "exec stderr");
    }

    [Fact]
    public async Task Restore_ChildKilledBySigterm_LeavesNoConfig()
    {
        await using var s = await Scenario.StartAsync();
        await s.SeedAsync();
        var backup = await RunToEnd(s.ExecCli(["backup", "/data/backup/r2.rrbak", "--passphrase-stdin"], "good_uri"));
        Assert.True(backup.ExitCode == 0, $"backup failed: {backup.StandardError}");

        await using var cli = s.ExecCli(["restore", "/data/backup/r2.rrbak", "--passphrase-stdin", "--force-overwrite"], "hang_uri");
        var probe = await s.ProbeToolChildAsync("mongorestore");

        await s.SignalAsync(probe.ChildPid);
        var exit = await cli.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.NotEqual(0, exit.ExitCode);
        await s.AssertOnlyHolderConfigRemainsAsync();
        s.AssertNoSecret(exit.StandardOutput, "exec stdout");
        s.AssertNoSecret(exit.StandardError, "exec stderr");
    }

    // ------------------------------------------------------------------------------------------------
    // Redaction with -e (kept from the original T019 test): unreachable host, credentialed URI.
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cli_RedactsPassword_WhenTargetHostIsUnreachable()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();

        var env = new Dictionary<string, string>
        {
            ["ROLERADAR_MONGO_CONNECTION_STRING"] =
                "mongodb://sensitiveuser:TopSecretPassword123@192.0.2.1:27017/rrtest?connectTimeoutMS=1000&serverSelectionTimeoutMS=2000",
            ["BACKUP_PW"] = "ArchivePassword123!"
        };

        var result = await ContainerTestHarness.RunCliAsync(["backup", "/tmp/backup.rrbak", "--passphrase-env", "BACKUP_PW"], environment: env);

        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("TopSecretPassword123", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitiveuser", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TopSecretPassword123", result.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitiveuser", result.StandardError, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================================================
    // Scenario fixture
    // ================================================================================================

    private sealed record ToolProbe(int ChildPid, int ParentPid, string ChildCmdline, string ParentCmdline, string ChildEnviron, string ConfigPath, string ConfigStat, string Raw);

    private sealed class Scenario : IAsyncDisposable
    {
        public required string Name { get; init; }
        public required string Root { get; init; }
        public required string Database { get; init; }
        public required string User { get; init; }
        public required string Password { get; init; }
        public required string Passphrase { get; init; }
        public string HolderConfig { get; private set; } = string.Empty;
        private RunningProcess? _holder;

        private string SecretsDir => Path.Combine(Root, "secrets");
        private string BackupDir => Path.Combine(Root, "backup");
        private string ProbeDir => Path.Combine(Root, "probe");

        public static async Task<Scenario> StartAsync()
        {
            await ContainerTestHarness.EnsureImagesBuiltAsync();

            var id = Guid.NewGuid().ToString("N");
            var s = new Scenario
            {
                Name = $"rr-credsec-{id[..12]}",
                Root = Path.Combine(Path.GetTempPath(), $"rr-credsec-{id}"),
                Database = $"rrtest_credsec_{id[..12]}",
                User = $"rrsec_{id[..8]}",
                Password = $"RrSecPw{id[..16]}",
                Passphrase = $"ArchivePhrase{id[..12]}"
            };

            Directory.CreateDirectory(s.SecretsDir);
            Directory.CreateDirectory(s.BackupDir);
            Directory.CreateDirectory(s.ProbeDir);

            // Per-test credential on the compose server (removed in DisposeAsync).
            var admin = new MongoClient(HostMongoUri).GetDatabase("admin");
            await admin.RunCommandAsync<BsonDocument>(new BsonDocument
            {
                ["createUser"] = s.User,
                ["pwd"] = s.Password,
                ["roles"] = new BsonArray { new BsonDocument { ["role"] = "root", ["db"] = "admin" } }
            });

            var cred = $"{s.User}:{s.Password}";
            WriteLf(Path.Combine(s.SecretsDir, "good_uri"),
                $"mongodb://{cred}@host.docker.internal:27017/?replicaSet=rs0&directConnection=true&authSource=admin");
            WriteLf(Path.Combine(s.SecretsDir, "hang_uri"),
                $"mongodb://{cred}@192.0.2.1:27017/?authSource=admin&connectTimeoutMS=1000&serverSelectionTimeoutMS=300000");
            WriteLf(Path.Combine(s.SecretsDir, "fail_uri"),
                $"mongodb://{cred}@192.0.2.1:27017/?authSource=admin&connectTimeoutMS=1000&serverSelectionTimeoutMS=2000");
            WriteProbeScripts(s.ProbeDir);

            // Holder: PID 1 is a CLI backup that blocks on the blackholed address, keeping /tmp alive.
            var args = new List<string> { "run", "--rm", "-i", "--name", s.Name };
            args.AddRange(HardeningFlags);
            args.AddRange([
                "--add-host", "host.docker.internal:host-gateway",
                "-v", $"{s.SecretsDir}:/run/secrets:ro",
                "-v", $"{s.BackupDir}:/data/backup:rw",
                "-e", "ROLERADAR_MONGO_CONNECTION_STRING_FILE=/run/secrets/hang_uri",
                "-e", $"ROLERADAR_MONGO_DATABASE={s.Database}",
                ContainerTestHarness.ResolveCliImage(),
                "backup", "/tmp/holder.rrbak", "--passphrase-stdin"
            ]);
            s._holder = RunningProcess.Start("docker", args, stdinLine: s.Passphrase, keepStdinOpen: true);

            var running = false;
            for (var i = 0; i < 50; i++)
            {
                var inspect = await Docker(["inspect", "-f", "{{.State.Running}}", s.Name]);
                if (inspect.ExitCode == 0 && inspect.StandardOutput.Trim() == "true")
                {
                    running = true;
                    break;
                }
                if (s._holder.HasExited)
                {
                    throw new InvalidOperationException($"Holder exited prematurely with code {s._holder.ExitCode}.\nStdout:\n{s._holder.StandardOutput}\nStderr:\n{s._holder.StandardError}");
                }
                await Task.Delay(100);
            }

            if (!running)
            {
                throw new TimeoutException($"Holder container {s.Name} did not become running within 5 s.\nStdout:\n{s._holder.StandardOutput}\nStderr:\n{s._holder.StandardError}");
            }

            var holderProbe = await s.ProbeAsync("probe-tools.sh", "mongodump", "pid1");
            s.HolderConfig = Path.GetFileName(holderProbe.ConfigPath);
            Assert.StartsWith("rr-tools-", s.HolderConfig, StringComparison.Ordinal);
            return s;
        }

        public RunningProcess ExecCli(string[] cliArgs, string uriFile, bool sendPassphrase = true)
        {
            var args = new List<string>
            {
                "exec", "-i",
                "-e", $"ROLERADAR_MONGO_CONNECTION_STRING_FILE=/run/secrets/{uriFile}",
                "-e", $"ROLERADAR_MONGO_DATABASE={Database}",
                Name, "/app/RoleRadar.Cli"
            };
            args.AddRange(cliArgs);
            return RunningProcess.Start("docker", args, stdinLine: sendPassphrase ? Passphrase : null, keepStdinOpen: false);
        }

        public Task<ToolProbe> ProbeToolChildAsync(string tool) => ProbeAsync("probe-tools.sh", tool, "exec");

        private async Task<ToolProbe> ProbeAsync(params string[] scriptAndArgs)
        {
            var r = await Sidecar(scriptAndArgs);
            Assert.True(r.ExitCode == 0, $"probe failed ({r.ExitCode}):\n{r.StandardOutput}\n{r.StandardError}");
            var kv = r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Contains('=', StringComparison.Ordinal))
                .Select(l => (Key: l[..l.IndexOf('=', StringComparison.Ordinal)], Value: l[(l.IndexOf('=', StringComparison.Ordinal) + 1)..]))
                .GroupBy(p => p.Key)
                .ToDictionary(g => g.Key, g => g.First().Value);
            return new ToolProbe(
                int.Parse(kv["CHILD_PID"], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(kv["PARENT_PID"], System.Globalization.CultureInfo.InvariantCulture),
                kv["CHILD_CMDLINE"], kv["PARENT_CMDLINE"], kv.GetValueOrDefault("CHILD_ENVIRON", string.Empty),
                kv["CONFIG_PATH"], kv["CONFIG_STAT"], r.StandardOutput);
        }

        public void AssertChildIsSafe(ToolProbe p)
        {
            // Argv: only a --config pointer on tmpfs; no URI, user, password or archive passphrase.
            Assert.Matches(@"--config=/tmp/rr-tools-[0-9a-f]{32}\.conf", p.ChildCmdline);
            Assert.DoesNotContain("mongodb://", p.ChildCmdline, StringComparison.OrdinalIgnoreCase);
            AssertNoSecret(p.ChildCmdline, "child /proc/<pid>/cmdline");
            AssertNoSecret(p.ParentCmdline, "CLI /proc/<pid>/cmdline");

            // Environ: the child inherits a minimal environment (no ROLERADAR_* and nothing secret).
            Assert.DoesNotContain("ROLERADAR_", p.ChildEnviron, StringComparison.Ordinal);
            AssertNoSecret(p.ChildEnviron, "child /proc/<pid>/environ");

            // Config file: created 0600 and owned by the unprivileged app user.
            Assert.Equal("600 1654:1654", p.ConfigStat);
        }

        public async Task SignalAsync(int pid)
        {
            var r = await Sidecar(["signal.sh", pid.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            Assert.True(r.ExitCode == 0, $"kill -TERM {pid} failed: {r.StandardError}");
        }

        public async Task AssertProcessGoneAsync(int pid)
        {
            var r = await Sidecar(["wait-gone.sh", pid.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            Assert.True(r.ExitCode == 0, $"pid {pid} still running after 15 s: {r.StandardOutput}");
        }

        public async Task AssertOnlyHolderConfigRemainsAsync()
        {
            var r = await Sidecar(["list-tmp.sh"]);
            Assert.True(r.ExitCode == 0, $"list-tmp failed: {r.StandardError}");
            var files = r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Assert.Equal([HolderConfig], files);
        }

        public void AssertNoSecret(string text, string where)
        {
            Assert.False(text.Contains(Password, StringComparison.Ordinal), $"database password found in {where}");
            Assert.False(text.Contains(User, StringComparison.Ordinal), $"database user found in {where}");
            Assert.False(text.Contains(Passphrase, StringComparison.Ordinal), $"archive passphrase found in {where}");
        }

        public async Task SeedAsync()
        {
            var db = new MongoClient(HostMongoUri).GetDatabase(Database);
            await db.GetCollection<BsonDocument>("notes").InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["owner_id"] = "credsec-owner",
                ["title"] = "Credential security fixture",
                ["body"] = "Fictional body",
                ["status"] = "active",
                ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["revision_count"] = 0,
                ["created_at"] = new BsonDateTime(DateTime.UtcNow),
                ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
            });
        }

        public async Task AssertDatabaseHasNoSecretAsync()
        {
            var db = new MongoClient(HostMongoUri).GetDatabase(Database);
            var names = await (await db.ListCollectionNamesAsync()).ToListAsync();
            Assert.Contains("audit_log", names);
            foreach (var name in names)
            {
                var docs = await db.GetCollection<BsonDocument>(name).Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
                foreach (var d in docs)
                {
                    AssertNoSecret(d.ToJson(), $"collection {name}");
                }
            }
        }

        public void AssertBackupDirectoryHasNoSecret()
        {
            var files = Directory.GetFiles(BackupDir, "*", SearchOption.AllDirectories);
            Assert.NotEmpty(files);
            foreach (var f in files)
            {
                var bytes = File.ReadAllBytes(f);
                AssertNoSecret(Encoding.UTF8.GetString(bytes), $"file {Path.GetFileName(f)}");
                AssertNoSecret(Encoding.Latin1.GetString(bytes), $"file {Path.GetFileName(f)} (latin1)");
            }
        }

        private Task<ProcessResult> Sidecar(string[] scriptAndArgs)
        {
            var args = new List<string> { "run", "--rm", "--network", "none", $"--pid=container:{Name}", "--user", "1654:1654" };
            args.AddRange(HardeningFlags);
            args.AddRange(["-v", $"{ProbeDir}:/probe:ro", SidecarImage, "bash", $"/probe/{scriptAndArgs[0]}"]);
            args.AddRange(scriptAndArgs.Skip(1));
            return Docker(args);
        }

        public async ValueTask DisposeAsync()
        {
            await Docker(["rm", "-f", Name]);
            if (_holder is not null)
            {
                await _holder.DisposeAsync();
            }

            try
            {
                var client = new MongoClient(HostMongoUri);
                await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("dropUser", User));
                await client.DropDatabaseAsync(Database);
            }
            catch (MongoException)
            {
                // Best effort cleanup of test-only state.
            }

            try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static void WriteLf(string path, string content) =>
            File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n", new UTF8Encoding(false));

        private static void WriteProbeScripts(string dir)
        {
            // Finds a running tool child (mongodump / mongorestore) and reports what an observer can see.
            WriteLf(Path.Combine(dir, "probe-tools.sh"), """
                #!/bin/bash
                tool="$1"; mode="$2"
                for i in $(seq 1 300); do
                  for p in /proc/[0-9]*; do
                    pid=${p#/proc/}
                    [ "$(cat "$p/comm" 2>/dev/null)" = "$tool" ] || continue
                    ppid=$(awk '/^PPid/{print $2}' "$p/status" 2>/dev/null)
                    if [ "$mode" = "pid1" ] && [ "$ppid" != "1" ]; then continue; fi
                    if [ "$mode" = "exec" ] && [ "$ppid" = "1" ]; then continue; fi
                    cfg=$(tr '\0' '\n' < "$p/cmdline" | sed -n 's/^--config=//p')
                    [ -n "$cfg" ] || continue
                    echo "CHILD_PID=$pid"
                    echo "PARENT_PID=$ppid"
                    echo "CHILD_CMDLINE=$(tr '\0' ' ' < "$p/cmdline")"
                    echo "PARENT_CMDLINE=$(tr '\0' ' ' < "/proc/$ppid/cmdline")"
                    echo "CHILD_ENVIRON=$(tr '\0' ' ' < "$p/environ")"
                    echo "CONFIG_PATH=$cfg"
                    echo "CONFIG_STAT=$(stat -c '%a %u:%g' "/proc/1/root$cfg")"
                    exit 0
                  done
                  sleep 0.05
                done
                echo "TIMEOUT waiting for $tool ($mode)"; exit 1
                """);
            WriteLf(Path.Combine(dir, "signal.sh"), """
                #!/bin/bash
                kill -TERM "$1"
                for i in $(seq 1 10); do
                  kill -0 "$1" 2>/dev/null || exit 0
                  sleep 0.05
                done
                kill -TERM "$1" 2>/dev/null || true
                """);
            WriteLf(Path.Combine(dir, "wait-gone.sh"), """
                #!/bin/bash
                for i in $(seq 1 300); do
                  [ -d "/proc/$1" ] || exit 0
                  # A zombie has already released its resources; treat it as gone.
                  grep -q '^State:.*Z' "/proc/$1/status" 2>/dev/null && exit 0
                  sleep 0.05
                done
                cat "/proc/$1/status"; exit 1
                """);
            WriteLf(Path.Combine(dir, "list-tmp.sh"), """
                #!/bin/bash
                ls -1A /proc/1/root/tmp | grep '^rr-tools-' || true
                """);
        }
    }

    // ================================================================================================
    // Process helpers (ArgumentList, so nothing is re-quoted through a shell)
    // ================================================================================================

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private static async Task<ProcessResult> RunToEnd(RunningProcess p)
    {
        await using (p)
        {
            return await p.WaitAsync(TimeSpan.FromSeconds(120));
        }
    }

    private static async Task<ProcessResult> Docker(IEnumerable<string> args)
    {
        await using var p = RunningProcess.Start("docker", args, stdinLine: null, keepStdinOpen: false);
        return await p.WaitAsync(TimeSpan.FromSeconds(120));
    }

    private sealed class RunningProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _out = new();
        private readonly StringBuilder _err = new();

        private RunningProcess(Process process) => _process = process;

        public bool HasExited => _process.HasExited;
        public int ExitCode => _process.ExitCode;
        public string StandardOutput { get { lock (_out) return _out.ToString(); } }
        public string StandardError { get { lock (_err) return _err.ToString(); } }

        public static RunningProcess Start(string file, IEnumerable<string> args, string? stdinLine, bool keepStdinOpen)
        {
            var psi = new ProcessStartInfo(file)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            var rp = new RunningProcess(new Process { StartInfo = psi });
            rp._process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (rp._out) { rp._out.AppendLine(e.Data); } } };
            rp._process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (rp._err) { rp._err.AppendLine(e.Data); } } };
            rp._process.Start();
            rp._process.BeginOutputReadLine();
            rp._process.BeginErrorReadLine();

            if (stdinLine is not null)
            {
                rp._process.StandardInput.Write(stdinLine + "\n");
                rp._process.StandardInput.Flush();
            }

            if (!keepStdinOpen)
            {
                rp._process.StandardInput.Close();
            }

            return rp;
        }

        public async Task<ProcessResult> WaitAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await _process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Process did not exit within {timeout.TotalSeconds:F0} s.\nstdout:\n{_out}\nstderr:\n{_err}");
            }

            _process.WaitForExit(); // flush async output handlers
            lock (_out)
            {
                lock (_err)
                {
                    return new ProcessResult(_process.ExitCode, _out.ToString(), _err.ToString());
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
