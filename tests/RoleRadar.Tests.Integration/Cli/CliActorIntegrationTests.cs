using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Cli.Demo;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Cli;

/// <summary>
/// Feature 013 (FR-016): every audit entry a CLI command writes names who did it. The commands run as the built CLI
/// against a throwaway Testcontainers database, except the demo seed, which needs the demo database name and so runs
/// in-process against its own container, as the 012 tests do. No test touches a developer's Mongo. Each assertion is that the actor starts with <c>cli-</c> (or is exactly <c>demo-seed</c>) and is
/// not empty or a placeholder.
/// </summary>
public sealed class CliActorIntegrationTests : IAsyncLifetime
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(90);

    private static readonly string[] Placeholders =
        ["api-user", "agent", "system", "unknown", "user", "cli", "cli-", "actor", "default", "test", "null", "none", "n/a", "-"];

    private readonly SharedMongoFixture _sharedMongo;
    private string _databaseName = string.Empty;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;
    private string _tempDirectory = string.Empty;

    public CliActorIntegrationTests(SharedMongoFixture sharedMongo)
    {
        _sharedMongo = sharedMongo;
    }

    public ValueTask InitializeAsync()
    {
        _databaseName = _sharedMongo.CreateDatabaseName("rrtest_cli_actor");
        MongoBootstrapAndIndexes.RegisterSerializers();
        _client = new MongoClient(_sharedMongo.ConnectionString);
        _database = _client.GetDatabase(_databaseName);
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"rr_cli_actor_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _sharedMongo.DropDatabaseAsync(_databaseName);
        }
        finally
        {
            _client.Dispose();
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(
        string arguments,
        IReadOnlyDictionary<string, string> environment)
    {
        var dllPath = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Cli.dll");
        if (!File.Exists(dllPath))
        {
            dllPath = typeof(RoleRadar.Cli.Program).Assembly.Location;
        }

        Assert.True(File.Exists(dllPath), $"Expected built CLI assembly at '{dllPath}'.");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{dllPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var (key, value) in environment)
        {
            psi.Environment[key] = value;
        }

        using var proc = Process.Start(psi)!;
        Assert.NotNull(proc);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await proc.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"'{arguments}' did not finish within {ProcessTimeout.TotalSeconds:0} s.");
        }

        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string arguments) =>
        await RunCliAsync(arguments, new Dictionary<string, string>(_sharedMongo.CreateProcessEnvironment(_databaseName)));

    private static async Task<List<AuditRow>> ReadAuditAsync(IMongoDatabase database)
    {
        var documents = await database
            .GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName)
            .Find(FilterDefinition<BsonDocument>.Empty)
            .ToListAsync(TestContext.Current.CancellationToken);

        return
        [
            .. documents.Select(d => new AuditRow(
                d.GetValue("actor", BsonNull.Value).IsString ? d["actor"].AsString : null,
                d.GetValue("operation", string.Empty).ToString() ?? string.Empty,
                d.GetValue("entity_type", string.Empty).ToString() ?? string.Empty))
        ];
    }

    private sealed record AuditRow(string? Actor, string Operation, string EntityType)
    {
        public override string ToString() => $"{EntityType}/{Operation} actor='{Actor ?? "<missing>"}'";
    }

    private static void AssertEveryActor(IReadOnlyCollection<AuditRow> rows, Func<string, bool> accepts, string expectation)
    {
        Assert.NotEmpty(rows);

        var seen = rows.GroupBy(r => r.Actor ?? "<missing>").Select(g => $"{g.Key} x{g.Count()}");
        TestContext.Current.TestOutputHelper?.WriteLine($"{rows.Count} audit entries; actors: {string.Join(", ", seen)}");

        var bad = rows
            .Where(r => string.IsNullOrWhiteSpace(r.Actor)
                        || Placeholders.Contains(r.Actor, StringComparer.OrdinalIgnoreCase)
                        || !accepts(r.Actor))
            .Select(r => r.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(bad.Length == 0, $"Expected every audit actor to {expectation}. Offenders: {string.Join("; ", bad)}");
    }

    private static bool IsCliActor(string actor) => actor.StartsWith("cli-", StringComparison.Ordinal) && actor.Length > "cli-".Length;

    [Fact]
    public async Task ContactAddAndRedact_WriteAuditEntries_WithACliActor()
    {
        var (addExit, addOut, addErr) = await RunAsync("contact add --name \"Pat Fictional\" --role Recruiter --company \"Acme Fictional\" --json");
        Assert.True(addExit == 0, $"contact add exited {addExit}; stderr: {addErr}");

        string contactId;
        using (var document = JsonDocument.Parse(addOut))
        {
            contactId = document.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        }

        var (redactExit, _, redactErr) = await RunAsync($"contact redact --id {contactId} --force --json");
        Assert.True(redactExit == 0, $"contact redact exited {redactExit}; stderr: {redactErr}");

        var rows = await ReadAuditAsync(_database);

        Assert.True(rows.Count >= 2, $"Expected at least an add and a redact entry, found {rows.Count}: {string.Join("; ", rows)}");
        AssertEveryActor(rows, IsCliActor, "start with 'cli-'");
    }

    [Fact]
    public async Task MarkdownImport_WritesAuditEntries_WithACliActor()
    {
        var file = Path.Combine(_tempDirectory, "fictional-notes.md");
        await File.WriteAllTextAsync(
            file,
            "# Fictional Heading One\nFirst fictional body.\n\n# Fictional Heading Two\nSecond fictional body.\n",
            TestContext.Current.CancellationToken);

        var (exit, _, stderr) = await RunAsync($"import \"{file}\"");
        Assert.True(exit == 0, $"import exited {exit}; stderr: {stderr}");

        var rows = await ReadAuditAsync(_database);

        AssertEveryActor(rows, IsCliActor, "start with 'cli-'");
    }

    [Fact]
    public async Task TrackerImportApply_WritesAuditEntries_WithACliActor_AndDryRunWritesNone()
    {
        var bundle = CreateFictionalBundleDirectory();

        var (dryExit, _, dryErr) = await RunAsync($"tracker-import \"{bundle}\" --default-date 2026-10-24");
        Assert.True(dryExit == 0, $"tracker-import dry run exited {dryExit}; stderr: {dryErr}");
        Assert.Empty(await ReadAuditAsync(_database));

        var (applyExit, applyOut, applyErr) = await RunAsync($"tracker-import \"{bundle}\" --default-date 2026-10-24 --apply");
        Assert.True(applyExit == 0, $"tracker-import --apply exited {applyExit}; stdout: {applyOut}; stderr: {applyErr}");

        var rows = await ReadAuditAsync(_database);

        AssertEveryActor(rows, IsCliActor, "start with 'cli-'");
    }

    [Fact]
    public async Task DemoSeed_WritesAuditEntries_WithTheDemoSeedActor_InItsOwnThrowawayDatabase()
    {
        // The seeder is what `demo seed` runs after its guard. It is called directly because the architecture rule
        // (DemoArchitectureTests) lets only the Demo test folders call the explicit-host guard overload. The database
        // carries the demo name inside its own container, so nothing here can reach a developer's Mongo.
        await using var host = new ODataTestHost(DemoModeGuard.DemoDatabaseName, "local-owner");
        await host.StartDatabaseAsync();

        var connection = host.ContainerConnectionString;
        await using var services = RoleRadar.Cli.Program.BuildServiceProvider(s => s.Configure<MongoOptions>(o =>
        {
            o.ConnectionString = connection;
            o.DatabaseName = DemoModeGuard.DemoDatabaseName;
        }));

        var report = await new DemoSeeder(services).SeedAsync(new DateOnly(2026, 9, 30), TestContext.Current.CancellationToken);
        Assert.False(report.AlreadySeeded);

        var rows = await ReadAuditAsync(host.Database);

        AssertEveryActor(rows, actor => string.Equals(actor, "demo-seed", StringComparison.Ordinal), "equal 'demo-seed'");
    }

    private string CreateFictionalBundleDirectory()
    {
        var dir = Path.Combine(_tempDirectory, "bundle");
        Directory.CreateDirectory(dir);

        File.WriteAllText(Path.Combine(dir, "watchlist-companies.csv"), "company,proposed_status,text,decision,corrected_notes\r\nAcme Corp,target,Fictional note,,");
        File.WriteAllText(Path.Combine(dir, "company-aliases.csv"), "alias,canonical\r\nAcme,Acme Corp");
        File.WriteAllText(Path.Combine(dir, "postings.json"), "{}");
        File.WriteAllText(
            Path.Combine(dir, "appendix-rows.csv"),
            "source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes\r\naggregator,screened_out,Acme Corp,Software Architect,101,,,yes,Details,2026-10-15,no-remote,no-remote,never,false,,,,");
        File.WriteAllText(
            Path.Combine(dir, "applications.csv"),
            "kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes\r\n" +
            "standalone_note,Acme Corp,Fictional Strategy Memo,,2026-03-12,,,,,,,,,Fictional architecture notes,,\r\n");

        return dir;
    }
}
