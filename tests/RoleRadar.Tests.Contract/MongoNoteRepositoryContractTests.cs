using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;
using RoleRadar.Infrastructure.Mongo;
using Testcontainers.MongoDb;
using Xunit;

namespace RoleRadar.Tests.Contract;

public sealed class MongoNoteRepositoryContractTests : IAsyncLifetime
{
    private readonly MongoDbContainer _mongoContainer = new MongoDbBuilder("mongo:8.0.34")
        .WithReplicaSet("rs0")
        .Build();

    private MongoClient _client = null!;
    private MongoOptions _options = null!;

    public async ValueTask InitializeAsync()
    {
        await _mongoContainer.StartAsync();
        _options = new MongoOptions
        {
            ConnectionString = _mongoContainer.GetConnectionString(),
            DatabaseName = $"roleradar_contract_{Guid.CreateVersion7():N}",
            ReadinessTimeout = TimeSpan.FromSeconds(5)
        };

        MongoBootstrapAndIndexes.RegisterSerializers();
        _client = new MongoClient(_options.ConnectionString);
        await MongoBootstrapAndIndexes.EnsureIndexesAsync(
            _client.GetDatabase(_options.DatabaseName),
            CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        await _mongoContainer.DisposeAsync();
    }

    private MongoNoteRepository CreateRepository(Func<string, CancellationToken, Task>? faultInjectionHook = null)
    {
        var probe = new MongoReadinessProbe(_client, Options.Create(_options), NullLogger<MongoReadinessProbe>.Instance);
        return new MongoNoteRepository(
            _client,
            Options.Create(_options),
            probe,
            NullLogger<MongoNoteRepository>.Instance,
            faultInjectionHook);
    }

    [Fact]
    public async Task Create_Get_Append_ReplaceSection_And_Archive_CommitAtomicallyWithAuditAndRevisions()
    {
        using var repo = CreateRepository();
        var now = new DateTime(2026, 10, 17, 16, 0, 0, DateTimeKind.Utc);
        var owner = OwnerId.Default.Value;

        var note = Note.Create(
            ownerId: owner,
            title: "Fictional Architecture Note",
            body: "# Overview\r\nInitial overview text.\r\n## Details\r\nInitial details.\r\n",
            tags: ["architecture", "dotnet"],
            area: "criteria",
            provenance: new ProvenanceMarker(ProvenanceKind.Stated, "2026-10-17"),
            createdAtUtc: now);

        var createAudit = AuditLogEntry.CreateForCreate(
            ownerId: owner,
            actor: "contract-test",
            targetNoteId: note.Id,
            resultingVersionToken: note.VersionToken,
            occurredAtUtc: now,
            afterSummary: AuditContentSummary.FromNote(note));

        var createResult = await repo.ExecuteInTransactionAsync(
            (tx, ct) => tx.CreateAsync(note, createAudit, ct));
        Assert.True(createResult.IsSuccess);

        // Append atomically server-side without caller version token -> NO NoteRevision created
        var appendResult = await repo.ExecuteInTransactionAsync(
            (tx, ct) => tx.AppendAsync(owner, note.Id, "Appended line.\r\n", "contract-test", now.AddMinutes(1), ct));
        Assert.True(appendResult.IsSuccess);
        Assert.Null(appendResult.Value!.Revision);

        var revisionsAfterAppend = await repo.GetRevisionsAsync(owner, note.Id);
        Assert.True(revisionsAfterAppend.IsSuccess);
        Assert.Empty(revisionsAfterAppend.Value!);

        // Replace section with valid version token -> creates NoteRevision #1
        var afterAppendNote = appendResult.Value.Note;
        var expectedToken = afterAppendNote.VersionToken;
        var beforeReplaceSummary = AuditContentSummary.FromNote(afterAppendNote);
        var revision = afterAppendNote.ReplaceBodyWithRevision(
            newBody: "# Overview\r\nReplaced overview text.\r\n## Details\r\nInitial details.\r\nAppended line.\r\n",
            reason: NoteRevision.ReasonReplaceSection,
            occurredAtUtc: now.AddMinutes(2));

        var replaceAudit = AuditLogEntry.CreateForReplaceSection(
            ownerId: owner,
            actor: "contract-test",
            targetNoteId: note.Id,
            noteRevisionId: revision.Id,
            resultingVersionToken: afterAppendNote.VersionToken,
            occurredAtUtc: now.AddMinutes(2),
            beforeSummary: beforeReplaceSummary,
            afterSummary: AuditContentSummary.FromNote(afterAppendNote));

        var replaceResult = await repo.ExecuteInTransactionAsync(
            (tx, ct) => tx.ReplaceSectionAsync(afterAppendNote, expectedToken, revision, replaceAudit, ct));
        Assert.True(replaceResult.IsSuccess);

        // Stale version token returns typed ErrorCode.StaleVersionToken without modifying stored note
        var staleResult = await repo.ExecuteInTransactionAsync(
            (tx, ct) => tx.ReplaceSectionAsync(afterAppendNote, expectedToken, revision, replaceAudit, ct));
        Assert.False(staleResult.IsSuccess);
        Assert.Equal(ErrorCode.StaleVersionToken, staleResult.Error!.Value.Code);

        var revisionsAfterReplace = await repo.GetRevisionsAsync(owner, note.Id);
        Assert.Single(revisionsAfterReplace.Value!);
        Assert.Equal(1, revisionsAfterReplace.Value![0].RevisionNumber);

        var audits = await repo.GetAuditEntriesAsync(owner, note.Id);
        Assert.Equal(3, audits.Value!.Count);
        Assert.Equal(
            [AuditOperation.NoteCreate, AuditOperation.NoteAppend, AuditOperation.NoteReplaceSection],
            audits.Value.Select(a => a.Operation).ToArray());
    }

    [Fact]
    public async Task FaultInjectedMidTransaction_RollsBackNoteAndRevision_LeavingZeroPartialWrites()
    {
        var owner = OwnerId.Default.Value;
        var now = new DateTime(2026, 10, 17, 16, 30, 0, DateTimeKind.Utc);

        using var repoWithFaultBeforeAudit = CreateRepository((stage, _) =>
        {
            if (stage == "before_audit_insert")
            {
                throw new InvalidOperationException("Simulated mid-transaction storage fault before audit_log insert.");
            }
            return Task.CompletedTask;
        });

        var note = Note.Create(
            ownerId: owner,
            title: "Should Roll Back Completely",
            body: "# Atomic Test\nBody content\n",
            tags: ["atomic"],
            area: "runbook",
            provenance: new ProvenanceMarker(ProvenanceKind.Stated, "2026-10-17"),
            createdAtUtc: now);

        var audit = AuditLogEntry.CreateForCreate(
            ownerId: owner,
            actor: "fault-test",
            targetNoteId: note.Id,
            resultingVersionToken: note.VersionToken,
            occurredAtUtc: now,
            afterSummary: AuditContentSummary.FromNote(note));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repoWithFaultBeforeAudit.ExecuteInTransactionAsync((tx, ct) => tx.CreateAsync(note, audit, ct)));

        using var cleanRepo = CreateRepository();
        var fetched = await cleanRepo.GetByIdAsync(owner, note.Id);
        Assert.False(fetched.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, fetched.Error!.Value.Code);

        var audits = await cleanRepo.GetAuditEntriesAsync(owner, note.Id);
        Assert.Empty(audits.Value!);
    }

    [Fact]
    public async Task UpsertImportedSection_DetectsPostImportEditsAsConflict_AndPreservesStoredNote()
    {
        using var repo = CreateRepository();
        var owner = OwnerId.Default.Value;
        var now = new DateTime(2026, 10, 17, 17, 0, 0, DateTimeKind.Utc);

        var initialReq = new UpsertImportedSectionRequest(
            OwnerId: owner,
            SourceFileId: "fictional-memory-primary.md",
            HeadingPath: "# Root / ## Section A",
            OccurrenceIndex: 0,
            SectionOrderIndex: 1,
            Title: "Section A",
            Body: "## Section A\r\nOriginal imported text.\r\n",
            Tags: ["imported"],
            Area: "criteria",
            Provenance: new ProvenanceMarker(ProvenanceKind.Stated, "2026-01-10"),
            ResolvedDefaultDate: "2026-10-17",
            Actor: "cli-import",
            OccurredAtUtc: now);

        var firstImport = await repo.ExecuteInTransactionAsync(
            (tx, ct) => tx.UpsertImportedSectionAsync(initialReq, ct));
        Assert.True(firstImport.IsSuccess);
        var importedNoteId = firstImport.Value!.Note.Id;

        // Simulate post-import agent append
        var append = await repo.ExecuteInTransactionAsync(
            (tx, ct) => tx.AppendAsync(owner, importedNoteId, "Agent post-import note.\r\n", "mcp-stdio", now.AddMinutes(5), ct));
        Assert.True(append.IsSuccess);

        // Re-import with modified source text -> must return ErrorCode.ImportConflict and NOT overwrite
        var secondReq = initialReq with
        {
            Body = "## Section A\r\nModified source file text.\r\n",
            OccurredAtUtc = now.AddMinutes(10)
        };

        var conflictResult = await repo.ExecuteInTransactionAsync(
            (tx, ct) => tx.UpsertImportedSectionAsync(secondReq, ct));
        Assert.False(conflictResult.IsSuccess);
        Assert.Equal(ErrorCode.ImportConflict, conflictResult.Error!.Value.Code);

        var storedAfterConflict = await repo.GetByIdAsync(owner, importedNoteId);
        Assert.True(storedAfterConflict.IsSuccess);
        Assert.Contains("Agent post-import note.", storedAfterConflict.Value!.Body);
    }
}
