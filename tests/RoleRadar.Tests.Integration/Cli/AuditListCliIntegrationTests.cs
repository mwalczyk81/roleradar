using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Cli;

/// <summary>
/// Feature 013 (FR-020, SC-009, contracts/cli-audit-list.md): <c>roleradar audit list</c> run as the built CLI against a
/// throwaway Testcontainers database. It prints newest first, never prints appended text or before and after values,
/// neutralizes control characters, exits 0, 1 or 2 as the contract says, and writes nothing to any collection.
/// </summary>
public sealed class AuditListCliIntegrationTests : IAsyncLifetime
{
    private const string OtherOwner = "other-owner-fictional";
    private const string SentinelAppended = "SENTINEL-APPENDED-TEXT-77de";
    private const string SentinelBefore = "SENTINEL-BEFORE-VALUE-77de";
    private const string SentinelAfter = "SENTINEL-AFTER-VALUE-77de";
    private const string SentinelExcerpt = "SENTINEL-EXCERPT-77de";
    private const string SentinelPassword = "SENTINEL-DB-PASSWORD-77de";

    private static readonly DateTime Origin = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(90);

    private readonly SharedMongoFixture _sharedMongo;
    private string _databaseName = string.Empty;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    public AuditListCliIntegrationTests(SharedMongoFixture sharedMongo)
    {
        _sharedMongo = sharedMongo;
    }

    public async ValueTask InitializeAsync()
    {
        _databaseName = _sharedMongo.CreateDatabaseName("rrtest_audit_cli");
        MongoBootstrapAndIndexes.RegisterSerializers();
        _client = new MongoClient(_sharedMongo.ConnectionString);
        _database = _client.GetDatabase(_databaseName);

        // Creates every collection, so the "no collection count changed" check has more than the audit log to compare.
        await MongoBootstrapAndIndexes.EnsureIndexesAsync(_database, CancellationToken.None);
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
        }
    }

    private IMongoCollection<BsonDocument> Audit =>
        _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);

    private static BsonBinaryData Bin(Guid id) => new(id, GuidRepresentation.Standard);

    private static BsonDocument Entry(
        DateTime occurredAtUtc,
        string owner = OwnerId.DefaultValue,
        string entityType = "posting",
        BsonValue? entityId = null,
        string actor = "cli-user",
        string operation = "posting_transition",
        string? requestCorrelationId = null,
        string[]? changedFields = null)
    {
        var doc = new BsonDocument
        {
            ["_id"] = Bin(Guid.CreateVersion7()),
            ["owner_id"] = owner,
            ["actor"] = actor,
            ["operation"] = operation,
            ["entity_type"] = entityType,
            ["entity_id"] = entityId ?? Bin(Guid.CreateVersion7()),
            ["resulting_version_token"] = Bin(Guid.CreateVersion7()),
            ["occurred_at"] = new BsonDateTime(occurredAtUtc),
            ["appended_text"] = SentinelAppended,
            ["before_summary"] = new BsonDocument { ["body_hash"] = "h", ["byte_length"] = 1, ["status"] = "s", ["excerpt"] = SentinelExcerpt },
            ["after_summary"] = new BsonDocument { ["body_hash"] = "h", ["byte_length"] = 1, ["status"] = "s", ["excerpt"] = SentinelExcerpt },
            ["details"] = new BsonDocument
            {
                ["changed_fields"] = new BsonArray(changedFields ?? ["disposition", "reason"]),
                ["before"] = new BsonDocument { ["disposition"] = SentinelBefore },
                ["after"] = new BsonDocument { ["disposition"] = SentinelAfter },
            },
        };

        if (requestCorrelationId is not null)
        {
            doc["request_correlation_id"] = requestCorrelationId;
        }

        return doc;
    }

    private async Task SeedAsync(params BsonDocument[] entries) => await Audit.InsertManyAsync(entries);

    private Dictionary<string, string> ProcessEnvironment() => new(_sharedMongo.CreateProcessEnvironment(_databaseName));

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
            throw new TimeoutException($"'audit {arguments}' did not finish within {ProcessTimeout.TotalSeconds:0} s.");
        }

        return (proc.ExitCode, await stdoutTask, await stderrTask);
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunAuditAsync(string arguments) =>
        await RunCliAsync($"audit list {arguments}".TrimEnd(), ProcessEnvironment());

    private static string[] Lines(string output) =>
        output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();

    private static string[] Tokens(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static string Stamp(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private async Task<Dictionary<string, long>> CollectionCountsAsync()
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        using var names = await _database.ListCollectionNamesAsync();
        foreach (var name in await names.ToListAsync())
        {
            counts[name] = await _database.GetCollection<BsonDocument>(name).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        }

        return counts;
    }

    [Fact]
    public async Task Text_ListsNewestFirst_OneLinePerEntry_AndPrintsNextCursorWhenMoreExist()
    {
        var entityId = Guid.CreateVersion7();
        await SeedAsync(
            Entry(Origin, operation: "oldest", requestCorrelationId: "req-0000-aaaa"),
            Entry(Origin.AddMinutes(1), entityId: Bin(entityId), operation: "middle", actor: "subject-1", requestCorrelationId: "req-0001-bbbb"),
            Entry(Origin.AddMinutes(2), operation: "newest"),
            Entry(Origin.AddMinutes(3), owner: OtherOwner, operation: "someone-else"));

        var (exit, stdout, stderr) = await RunAuditAsync("--limit 2");

        Assert.True(exit == 0, $"exit {exit}; stderr: {stderr}");
        var lines = Lines(stdout);
        Assert.Equal(3, lines.Length);

        var newest = Tokens(lines[0]);
        Assert.Equal([Stamp(Origin.AddMinutes(2)), "posting", newest[2], "newest", "cli-user", "req=-"], newest);

        var middle = Tokens(lines[1]);
        Assert.Equal(
            [Stamp(Origin.AddMinutes(1)), "posting", entityId.ToString("D"), "middle", "subject-1", "req=req-0001-bbbb"],
            middle);

        Assert.StartsWith("next: ", lines[2], StringComparison.Ordinal);
        var cursor = lines[2]["next: ".Length..];
        Assert.False(string.IsNullOrWhiteSpace(cursor));
        Assert.DoesNotContain("someone-else", stdout, StringComparison.Ordinal);

        var (exitNext, stdoutNext, stderrNext) = await RunAuditAsync($"--limit 2 --cursor {cursor}");

        Assert.True(exitNext == 0, $"exit {exitNext}; stderr: {stderrNext}");
        var lastPage = Lines(stdoutNext);
        var oldest = Assert.Single(lastPage);
        Assert.Equal([Stamp(Origin), "posting", Tokens(oldest)[2], "oldest", "cli-user", "req=req-0000-aaaa"], Tokens(oldest));
        Assert.DoesNotContain("next:", stdoutNext, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Json_ReturnsTheOkEnvelope_WithItemsAndNextCursor()
    {
        await SeedAsync(
            Entry(Origin, requestCorrelationId: "req-0000-aaaa"),
            Entry(Origin.AddMinutes(1)),
            Entry(Origin.AddMinutes(2)));

        var (exit, stdout, stderr) = await RunAuditAsync("--json --limit 2");

        Assert.True(exit == 0, $"exit {exit}; stderr: {stderr}");
        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());

        var data = root.GetProperty("data");
        var items = data.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(Stamp(Origin.AddMinutes(2)), items[0].GetProperty("occurredAt").GetString());
        Assert.Equal(Stamp(Origin.AddMinutes(1)), items[1].GetProperty("occurredAt").GetString());

        var propertyNames = items[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["actor", "entityId", "entityType", "occurredAt", "operation", "requestCorrelationId"], propertyNames);
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("requestCorrelationId").ValueKind);

        var cursor = data.GetProperty("nextCursor").GetString();
        Assert.False(string.IsNullOrWhiteSpace(cursor));

        var (exitNext, stdoutNext, _) = await RunAuditAsync($"--json --limit 2 --cursor {cursor}");

        Assert.Equal(0, exitNext);
        using var next = JsonDocument.Parse(stdoutNext);
        var nextData = next.RootElement.GetProperty("data");
        var last = Assert.Single(nextData.GetProperty("items").EnumerateArray().ToArray());
        Assert.Equal("req-0000-aaaa", last.GetProperty("requestCorrelationId").GetString());
        Assert.Equal(JsonValueKind.Null, nextData.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Fields_AddsChangedFieldNames_AndNeverValues_InTextAndJson()
    {
        await SeedAsync(Entry(Origin, changedFields: ["disposition", "reason"]));

        var (plainExit, plainStdout, _) = await RunAuditAsync(string.Empty);
        var (textExit, textStdout, textStderr) = await RunAuditAsync("--fields");
        var (jsonExit, jsonStdout, jsonStderr) = await RunAuditAsync("--json --fields");
        var (plainJsonExit, plainJsonStdout, _) = await RunAuditAsync("--json");

        Assert.Equal(0, plainExit);
        Assert.DoesNotContain("fields=", plainStdout, StringComparison.Ordinal);

        Assert.True(textExit == 0, $"exit {textExit}; stderr: {textStderr}");
        var textLine = Assert.Single(Lines(textStdout));
        Assert.EndsWith(" fields=disposition,reason", textLine, StringComparison.Ordinal);

        Assert.True(jsonExit == 0, $"exit {jsonExit}; stderr: {jsonStderr}");
        using var withFields = JsonDocument.Parse(jsonStdout);
        var changed = withFields.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("changedFields");
        Assert.Equal(["disposition", "reason"], changed.EnumerateArray().Select(e => e.GetString()).ToArray());

        Assert.Equal(0, plainJsonExit);
        using var withoutFields = JsonDocument.Parse(plainJsonStdout);
        Assert.False(withoutFields.RootElement.GetProperty("data").GetProperty("items")[0].TryGetProperty("changedFields", out _));

        foreach (var output in new[] { textStdout, jsonStdout })
        {
            Assert.DoesNotContain(SentinelBefore, output, StringComparison.Ordinal);
            Assert.DoesNotContain(SentinelAfter, output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ControlCharacters_InStoredValues_PrintAsQuestionMarks_AndKeepOneLinePerEntry()
    {
        await SeedAsync(Entry(
            Origin,
            entityId: "id\u001b[31m\r\nx",
            actor: "\u001b[31mred\r\nactor",
            operation: "op\u0007x",
            requestCorrelationId: "req\u0007-0001\u007f"));

        var (exit, stdout, stderr) = await RunAuditAsync(string.Empty);

        Assert.True(exit == 0, $"exit {exit}; stderr: {stderr}");
        Assert.DoesNotContain("\u001b", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0007", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("\u007f", stdout, StringComparison.Ordinal);

        var line = Assert.Single(Lines(stdout));
        Assert.Equal(
            [Stamp(Origin), "posting", "id?[31m??x", "op?x", "?[31mred??actor", "req=req?-0001?"],
            Tokens(line));
    }

    [Fact]
    public async Task ControlCharacters_InJson_AreEscapedNotRaw_AndTheStoredValueSurvives()
    {
        const string storedActor = "\u001b[31mred\r\nactor";
        await SeedAsync(Entry(Origin, actor: storedActor));

        var (exit, stdout, stderr) = await RunAuditAsync("--json");

        Assert.True(exit == 0, $"exit {exit}; stderr: {stderr}");
        Assert.DoesNotContain("\u001b", stdout, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal(storedActor, document.RootElement.GetProperty("data").GetProperty("items")[0].GetProperty("actor").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("--json")]
    [InlineData("--fields")]
    [InlineData("--json --fields")]
    public async Task NoMode_PrintsAppendedText_OrBeforeAndAfterValues(string arguments)
    {
        await SeedAsync(Entry(Origin), Entry(Origin.AddMinutes(1)));

        var (exit, stdout, stderr) = await RunAuditAsync(arguments);

        Assert.True(exit == 0, $"exit {exit}; stderr: {stderr}");
        Assert.NotEmpty(stdout);
        foreach (var sentinel in new[] { SentinelAppended, SentinelBefore, SentinelAfter, SentinelExcerpt })
        {
            Assert.DoesNotContain(sentinel, stdout, StringComparison.Ordinal);
            Assert.DoesNotContain(sentinel, stderr, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task EmptyPage_ExitsZero_InTextAndJson()
    {
        var (textExit, textStdout, textStderr) = await RunAuditAsync(string.Empty);
        var (jsonExit, jsonStdout, jsonStderr) = await RunAuditAsync("--json");

        Assert.True(textExit == 0, $"exit {textExit}; stderr: {textStderr}");
        Assert.Empty(Lines(textStdout));

        Assert.True(jsonExit == 0, $"exit {jsonExit}; stderr: {jsonStderr}");
        using var document = JsonDocument.Parse(jsonStdout);
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(0, document.RootElement.GetProperty("data").GetProperty("items").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("data").GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task FiltersAndOffsets_AreAppliedFromTheOptions()
    {
        var entityId = Guid.CreateVersion7();
        await SeedAsync(
            Entry(Origin, entityType: "company", actor: "subject-1", operation: "a"),
            Entry(Origin.AddMinutes(1), entityType: "posting", entityId: Bin(entityId), actor: "cli-user", operation: "b"),
            Entry(Origin.AddMinutes(2), entityType: "posting", actor: "subject-1", operation: "c"));

        var byType = Lines((await RunAuditAsync("--entity-type company")).Stdout);
        var byEntity = Lines((await RunAuditAsync($"--entity-id {entityId:D}")).Stdout);
        var byActor = Lines((await RunAuditAsync("--actor subject-1")).Stdout);
        var window = Lines((await RunAuditAsync("--from 2026-11-09T14:01:00+02:00 --to 2026-11-09T14:02:00+02:00")).Stdout);

        Assert.Equal("a", Tokens(Assert.Single(byType))[3]);
        Assert.Equal("b", Tokens(Assert.Single(byEntity))[3]);
        Assert.Equal(["c", "a"], byActor.Select(l => Tokens(l)[3]).ToArray());
        Assert.Equal("b", Tokens(Assert.Single(window))[3]);
    }

    [Theory]
    [InlineData("--limit 0")]
    [InlineData("--limit 101")]
    [InlineData("--limit -5")]
    [InlineData("--limit abc")]
    [InlineData("--from 2026-11-09T13:00:00Z --to 2026-11-09T12:00:00Z")]
    [InlineData("--from 2026-11-09T12:00:00Z --to 2026-11-09T12:00:00Z")]
    [InlineData("--from 2026-11-09T12:00:00")]
    [InlineData("--to yesterday")]
    [InlineData("--cursor not-a-cursor")]
    [InlineData("--entity-type widget")]
    public async Task InvalidOptions_ExitTwo_WithAValidationError(string arguments)
    {
        var (exit, stdout, stderr) = await RunAuditAsync(arguments);

        Assert.Equal(2, exit);
        Assert.Empty(Lines(stdout));
        Assert.Contains("Error:", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("at RoleRadar.", stderr, StringComparison.Ordinal);

        var (jsonExit, jsonStdout, _) = await RunAuditAsync($"{arguments} --json");

        Assert.Equal(2, jsonExit);
        using var document = JsonDocument.Parse(jsonStdout);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("validation_error", document.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnreachableMongo_ExitsOne_WithoutTheConnectionStringCredential()
    {
        var environment = ProcessEnvironment();
        environment["ROLERADAR_MONGO_CONNECTION_STRING"] = $"mongodb://audituser:{SentinelPassword}@127.0.0.1:1/?authSource=admin";

        var (exit, stdout, stderr) = await RunCliAsync("audit list", environment);
        var (jsonExit, jsonStdout, jsonStderr) = await RunCliAsync("audit list --json", environment);

        Assert.Equal(1, exit);
        Assert.Contains("Error:", stderr, StringComparison.Ordinal);
        Assert.Equal(1, jsonExit);
        using var document = JsonDocument.Parse(jsonStdout);
        Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("service_unavailable", document.RootElement.GetProperty("error").GetProperty("code").GetString());

        foreach (var output in new[] { stdout, stderr, jsonStdout, jsonStderr })
        {
            Assert.DoesNotContain(SentinelPassword, output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("audituser", output, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("at RoleRadar.", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RunningTheCommand_ChangesNoCollectionCount_IncludingAuditLog()
    {
        await SeedAsync(Entry(Origin), Entry(Origin.AddMinutes(1)), Entry(Origin.AddMinutes(2), owner: OtherOwner));
        var before = await CollectionCountsAsync();

        Assert.True(before.Count > 1, "The database should hold more than one collection for this check to mean anything.");
        Assert.Equal(3, before[MongoBootstrapAndIndexes.AuditLogCollectionName]);

        // Each run must really reach the command (its exit code is checked), or "nothing changed" would hold vacuously.
        foreach (var (arguments, expectedExit) in new[]
                 {
                     (string.Empty, 0), ("--json", 0), ("--fields", 0), ("--limit 1", 0),
                     ("--limit 0", 2), ("--cursor nope", 2), ("--entity-type widget", 2),
                 })
        {
            var (exit, _, stderr) = await RunAuditAsync(arguments);
            Assert.True(exit == expectedExit, $"'audit list {arguments}' exited {exit}, expected {expectedExit}; stderr: {stderr}");
        }

        var unreachable = ProcessEnvironment();
        unreachable["ROLERADAR_MONGO_CONNECTION_STRING"] = "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=500";
        var (unreachableExit, _, _) = await RunCliAsync("audit list", unreachable);
        Assert.Equal(1, unreachableExit);

        var after = await CollectionCountsAsync();
        Assert.Equal(before.OrderBy(p => p.Key, StringComparer.Ordinal), after.OrderBy(p => p.Key, StringComparer.Ordinal));
    }
}
