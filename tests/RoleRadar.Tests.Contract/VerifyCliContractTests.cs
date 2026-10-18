using System.Diagnostics;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Repositories;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class VerifyCliContractTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _fixture;
    private readonly string _databaseName;
    private MongoClient _client = null!;
    private IMongoDatabase _database = null!;

    public VerifyCliContractTests(SharedMongoFixture fixture)
    {
        _fixture = fixture;
        _databaseName = _fixture.CreateDatabaseName("roleradar_verify_contract");
    }

    public async ValueTask InitializeAsync()
    {
        _client = new MongoClient(_fixture.ConnectionString);
        _database = _client.GetDatabase(_databaseName);
        await TrackingIndexInitializer.EnsureIndexesAsync(_database, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client is not null && !string.IsNullOrWhiteSpace(_databaseName))
            {
                await _client.DropDatabaseAsync(_databaseName);
            }
        }
        finally
        {
            _client?.Dispose();
        }
    }

    [Fact]
    public async Task Verify_WhenDatabaseIsClean_ReturnsExitCodeZero()
    {
        var cliDllPath = GetCliDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        // Seed valid company, posting and application
        var companyId = Guid.NewGuid();
        var postingId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        var companiesCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName);
        await companiesCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["name"] = "Integrity Clean Tech",
            ["status"] = "target",
            ["ownership_type"] = "public",
            ["category_tags"] = new BsonArray(),
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        var postingsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        await postingsCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(postingId, GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["company_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
            ["title"] = "Senior Verification Engineer",
            ["url"] = "https://example.com/jobs/1",
            ["normalized_url"] = "https://example.com/jobs/1",
            ["source_kind"] = "ats",
            ["disposition"] = "lead",
            ["resurface_policy"] = "never",
            ["culture_risk_level"] = "stable",
            ["first_seen_at"] = new BsonDateTime(DateTime.UtcNow),
            ["last_verified_at"] = new BsonDateTime(DateTime.UtcNow),
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        var (exitCode, stdout, stderr) = await RunCliAsync(cliDllPath, "verify --json", env);
        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal(0, root.GetProperty("data").GetProperty("anomalyCount").GetInt32());
    }

    [Fact]
    public async Task Verify_DetectsDanglingReferences_ReturnsNonZero_AndModifiesNothing()
    {
        var cliDllPath = GetCliDllPath();
        var env = _fixture.CreateProcessEnvironment(_databaseName);

        // 1. Seed valid baseline data through real handlers & repositories
        var companyRepo = new MongoCompanyRepository(_client, _database);
        var postingRepo = new MongoPostingRepository(_client, _database);
        var criterionRepo = new MongoCriterionRepository(_client, _database);
        var appRepo = new MongoApplicationRepository(_client, _database);
        var contactRepo = new MongoContactRepository(_client, _database);
        var searchRunRepo = new MongoSearchRunRepository(_client, _database);
        var auditCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        var context = new MongoTrackingTransactionContext(companyRepo, postingRepo, criterionRepo, appRepo, contactRepo, searchRunRepo, auditCol);
        var clock = SystemUtcClock.Instance;

        // Seed clean company
        var companyHandler = new RecordCompanyCommandHandler(context, clock);
        var companyRes = await companyHandler.HandleAsync(new RecordCompanyCommand(Name: "Baseline Integrity Tech", OwnershipType: "public", OwnerId: "owner-1"));
        Assert.True(companyRes.IsSuccess);
        var companyId = companyRes.Value!.Company.Id;

        // Seed root posting
        var postingHandler = new RecordPostingCommandHandler(context, clock);
        var rootPostingRes = await postingHandler.HandleAsync(new RecordPostingCommand(CompanyId: companyId, Title: "Root Engineer", SourceKind: "ats", AtsTenant: "cleanats", RequisitionId: "REQ-ROOT", OwnerId: "owner-1"));
        Assert.True(rootPostingRes.IsSuccess);
        var rootPostingId = rootPostingRes.Value!.Posting!.Id;

        // Seed duplicate posting referencing root
        var dupPostingRes = await postingHandler.HandleAsync(new RecordPostingCommand(CompanyId: companyId, Title: "Duplicate Engineer", SourceKind: "ats", AtsTenant: "cleanats", RequisitionId: "REQ-DUP", DuplicateOfId: rootPostingId, OwnerId: "owner-1"));
        Assert.True(dupPostingRes.IsSuccess);
        var dupPostingId = dupPostingRes.Value!.Posting!.Id;

        // Seed posting to be applied
        var appliedPostingRes = await postingHandler.HandleAsync(new RecordPostingCommand(CompanyId: companyId, Title: "Applied Engineer", SourceKind: "ats", AtsTenant: "cleanats", RequisitionId: "REQ-APP", OwnerId: "owner-1"));
        Assert.True(appliedPostingRes.IsSuccess);
        var appliedPostingId = appliedPostingRes.Value!.Posting!.Id;

        // Seed application
        var appHandler = new RecordApplicationCommandHandler(context, clock);
        var appRes = await appHandler.HandleAsync(new RecordApplicationCommand(PostingId: appliedPostingId, Action: "create", SubmittedDate: "2026-10-18", ResumeVariant: "tailored", OwnerId: "owner-1"));
        Assert.True(appRes.IsSuccess, $"Failed to create application: {appRes.Error?.Code} - {appRes.Error?.Message}");
        var applicationId = appRes.Value!.ApplicationId;

        // Seed posting with signal
        var signalPostingRes = await postingHandler.HandleAsync(new RecordPostingCommand(CompanyId: companyId, Title: "Signal Engineer", SourceKind: "ats", AtsTenant: "cleanats", RequisitionId: "REQ-SIG", Signals: new[] { new SignalInputDto("gone", "404", "2026-10-18T10:00:00Z") }, OwnerId: "owner-1"));
        Assert.True(signalPostingRes.IsSuccess);
        var signalPostingId = signalPostingRes.Value!.Posting!.Id;

        // Seed note referencing company, posting, and application
        var noteId = Guid.NewGuid();
        var notesCol = _database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
        await notesCol.InsertOneAsync(new BsonDocument
        {
            ["_id"] = new BsonBinaryData(noteId, GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["title"] = "Clean Linked Note",
            ["body"] = "Note body",
            ["area"] = "tech",
            ["status"] = "active",
            ["company_id"] = new BsonBinaryData(companyId, GuidRepresentation.Standard),
            ["posting_id"] = new BsonBinaryData(rootPostingId, GuidRepresentation.Standard),
            ["application_id"] = new BsonBinaryData(applicationId, GuidRepresentation.Standard),
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        });

        // Seed search run referencing the note
        var trackerLogHandler = new TrackerLogCommandHandler(context, clock);
        var searchRunRes = await trackerLogHandler.HandleAsync(new TrackerLogCommand(Action: "search_run", StartedAt: "2026-10-18T09:00:00Z", NewPostingsCount: 1, ReportNoteId: noteId, Notes: "Sweep report", OwnerId: "owner-1"));
        Assert.True(searchRunRes.IsSuccess);
        var searchRunId = searchRunRes.Value!.RecordId;

        // Seed applied posting WITHOUT application
        var danglingAppPostingRes = await postingHandler.HandleAsync(new RecordPostingCommand(CompanyId: companyId, Title: "Unlinked Applied Posting", SourceKind: "ats", AtsTenant: "cleanats", RequisitionId: "REQ-NO-APP", OwnerId: "owner-1"));
        Assert.True(danglingAppPostingRes.IsSuccess);
        var unlinkedAppliedPostingId = danglingAppPostingRes.Value!.Posting!.Id;
        // Mark disposition as applied directly on posting
        var postingsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);
        await postingsCol.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(unlinkedAppliedPostingId, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Update.Set("disposition", "applied"));

        // Now mutate REAL stored fields to trigger all 8 anomaly kinds:
        var nonExistentGuid = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard);

        // Anomaly 1: dangling_company_on_posting (break company_id on root posting)
        await postingsCol.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(rootPostingId, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Update.Set("company_id", nonExistentGuid));

        // Anomaly 2: dangling_duplicate_of_on_posting (break duplicate_of_id on dup posting)
        await postingsCol.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(dupPostingId, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Update.Set("duplicate_of_id", nonExistentGuid));

        // Anomaly 3: dangling_posting_on_application (break posting_id on application)
        var appsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName);
        await appsCol.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(applicationId, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Update.Set("posting_id", nonExistentGuid));

        // Anomaly 4: dangling_posting_on_signal (break posting_id on signal)
        var signalsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingSignalsCollectionName);
        await signalsCol.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("posting_id", new BsonBinaryData(signalPostingId, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Update.Set("posting_id", nonExistentGuid));

        // Anomaly 5, 6, 7: dangling_company_on_note, dangling_posting_on_note, dangling_application_on_note
        await notesCol.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(noteId, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Update
                .Set("company_id", nonExistentGuid)
                .Set("posting_id", nonExistentGuid)
                .Set("application_id", nonExistentGuid));

        // Anomaly 8: dangling_report_note_on_search_run (break report_note_id on search run)
        var searchRunsCol = _database.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName);
        await searchRunsCol.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(searchRunId, GuidRepresentation.Standard)),
            Builders<BsonDocument>.Update.Set("report_note_id", nonExistentGuid));

        // Snapshot counts before CLI run
        var countPostingsBefore = await postingsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        var countAppsBefore = await appsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        var countSignalsBefore = await signalsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        var countNotesBefore = await notesCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);
        var countRunsBefore = await searchRunsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty);

        // Run launched CLI
        var (exitCode, stdout, stderr) = await RunCliAsync(cliDllPath, "verify --json", env);
        Assert.Equal(1, exitCode);
        Assert.DoesNotContain("Unhandled exception", stderr, StringComparison.OrdinalIgnoreCase);

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        var data = root.GetProperty("data");
        Assert.Equal(10, data.GetProperty("anomalyCount").GetInt32()); // All 8 distinct kinds present (one note has 3 dangling fields + 2 unlinked applied postings + 5 other kinds = 10 total)

        var anomalyKinds = new HashSet<string>();
        foreach (var el in data.GetProperty("anomalies").EnumerateArray())
        {
            anomalyKinds.Add(el.GetProperty("kind").GetString()!);
        }

        // Assert every one of the 8 anomaly kinds is detected
        Assert.Contains("dangling_company_on_posting", anomalyKinds);
        Assert.Contains("dangling_duplicate_of_on_posting", anomalyKinds);
        Assert.Contains("dangling_posting_on_application", anomalyKinds);
        Assert.Contains("dangling_posting_on_signal", anomalyKinds);
        Assert.Contains("dangling_company_on_note", anomalyKinds);
        Assert.Contains("dangling_posting_on_note", anomalyKinds);
        Assert.Contains("dangling_application_on_note", anomalyKinds);
        Assert.Contains("dangling_report_note_on_search_run", anomalyKinds);
        Assert.Contains("applied_posting_without_application", anomalyKinds);

        // Assert read-only guarantee: zero documents mutated or deleted
        Assert.Equal(countPostingsBefore, await postingsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty));
        Assert.Equal(countAppsBefore, await appsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty));
        Assert.Equal(countSignalsBefore, await signalsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty));
        Assert.Equal(countNotesBefore, await notesCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty));
        Assert.Equal(countRunsBefore, await searchRunsCol.CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty));
    }

    private static string GetCliDllPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "RoleRadar.Cli.dll");
        if (!File.Exists(path))
        {
            var slnRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var candidate = Path.Combine(slnRoot, "src", "RoleRadar.Cli", "bin", "Release", "net10.0", "RoleRadar.Cli.dll");
            if (File.Exists(candidate))
            {
                path = candidate;
            }
            else
            {
                candidate = Path.Combine(slnRoot, "src", "RoleRadar.Cli", "bin", "Debug", "net10.0", "RoleRadar.Cli.dll");
                if (File.Exists(candidate))
                {
                    path = candidate;
                }
            }
        }

        Assert.True(File.Exists(path), $"Expected CLI assembly at '{path}'.");
        return path;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCliAsync(
        string dllPath,
        string arguments,
        IReadOnlyDictionary<string, string> env)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{dllPath}\" {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var (k, v) in env)
        {
            psi.Environment[k] = v;
        }

        using var proc = Process.Start(psi)!;
        Assert.NotNull(proc);

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        await proc.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return (proc.ExitCode, stdout, stderr);
    }
}
