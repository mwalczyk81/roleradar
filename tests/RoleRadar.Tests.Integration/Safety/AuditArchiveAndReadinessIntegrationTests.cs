using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Import.Commands;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Application.Notes.Queries;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure;
using RoleRadar.Mcp.Stdio.Tools;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Safety;

public sealed class AuditArchiveAndReadinessIntegrationTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _sharedMongo;
    private readonly string _databaseName;
    private MongoClient? _sharedClient;

    public AuditArchiveAndReadinessIntegrationTests(SharedMongoFixture sharedMongo)
    {
        ArgumentNullException.ThrowIfNull(sharedMongo);
        _sharedMongo = sharedMongo;
        _databaseName = $"roleradar_phase6_{Guid.CreateVersion7():N}";
    }

    public ValueTask InitializeAsync()
    {
        _sharedClient = new MongoClient(_sharedMongo.ConnectionString);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_sharedClient != null)
            {
                await _sharedClient.DropDatabaseAsync(_databaseName);
            }
        }
        finally
        {
            _sharedClient?.Dispose();
        }
    }

    private ServiceProvider BuildServiceProvider(string connectionString, string databaseName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRoleRadarApplication();
        services.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = connectionString;
            opts.DatabaseName = databaseName;
            opts.ReadinessTimeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<NoteMcpTools>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ArchiveNote_BodyAndRevisionsUntouched_HiddenFromDefaultSearch_ReturnedByGetAndIncludeArchived_TokenRotates_StaleTokenRejected_Idempotent()
    {
        await using var provider = BuildServiceProvider(_sharedMongo.ConnectionString, _databaseName);
        var repo = provider.GetRequiredService<INoteRepository>();
        var createHandler = provider.GetRequiredService<ICommandHandler<CreateNoteCommand, NoteCreatedDto>>();
        var getHandler = provider.GetRequiredService<IQueryHandler<GetNoteQuery, NoteDetailDto>>();
        var searchHandler = provider.GetRequiredService<IQueryHandler<SearchNotesQuery, NoteSearchResponseDto>>();
        var archiveHandler = provider.GetRequiredService<ICommandHandler<ArchiveNoteCommand, NoteArchivedDto>>();

        var now = new DateTime(2026, 10, 17, 19, 0, 0, DateTimeKind.Utc);
        var originalBody = "# Important Note\n## Criteria\nMust be remote 180k.\n";
        var created = await createHandler.HandleAsync(new CreateNoteCommand(
            Title: "Archive Contract Target",
            Body: originalBody,
            Area: "criteria",
            Tags: ["remote", "contract"],
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: null));
        Assert.True(created.IsSuccess);
        var noteId = created.Value!.Id;
        var initialToken = created.Value.VersionToken;

        // 1. Initial note is Active and returned by default search
        var searchActive = await searchHandler.HandleAsync(new SearchNotesQuery(Query: "remote", IncludeArchived: false));
        Assert.True(searchActive.IsSuccess);
        Assert.Contains(searchActive.Value!.Results, r => r.Id == noteId);

        // 2. Reject stale expectedVersionToken
        var bogusToken = Guid.CreateVersion7();
        var staleArchiveResult = await archiveHandler.HandleAsync(new ArchiveNoteCommand(
            Id: noteId,
            ExpectedVersionToken: bogusToken));
        Assert.False(staleArchiveResult.IsSuccess);
        Assert.Equal(ErrorCode.StaleVersionToken, staleArchiveResult.Error!.Value.Code);

        // 3. Archive with valid expectedVersionToken (or null)
        var archiveResult = await archiveHandler.HandleAsync(new ArchiveNoteCommand(
            Id: noteId,
            ExpectedVersionToken: initialToken));
        Assert.True(archiveResult.IsSuccess);
        Assert.Equal("archived", archiveResult.Value!.Status);
        Assert.NotEqual(initialToken, archiveResult.Value.VersionToken);
        var archivedToken = archiveResult.Value.VersionToken;

        // 4. Body and revisions are untouched
        var noteGetResult = await getHandler.HandleAsync(new GetNoteQuery(noteId));
        Assert.True(noteGetResult.IsSuccess);
        Assert.Equal("archived", noteGetResult.Value!.Status);
        Assert.Equal(originalBody, noteGetResult.Value.Body);
        Assert.Equal(archivedToken, noteGetResult.Value.VersionToken);
        Assert.Equal(0, noteGetResult.Value.RevisionCount);

        var revisionsResult = await repo.GetRevisionsAsync(OwnerId.Default.Value, noteId);
        Assert.True(revisionsResult.IsSuccess);
        Assert.Empty(revisionsResult.Value!); // Zero revisions created on archive

        // 5. Hidden from default search (includeArchived: false)
        var searchExcludingArchived = await searchHandler.HandleAsync(new SearchNotesQuery(Query: "remote", IncludeArchived: false));
        Assert.True(searchExcludingArchived.IsSuccess);
        Assert.DoesNotContain(searchExcludingArchived.Value!.Results, r => r.Id == noteId);

        // 6. Returned by note_search when includeArchived: true
        var searchIncludingArchived = await searchHandler.HandleAsync(new SearchNotesQuery(Query: "remote", IncludeArchived: true));
        Assert.True(searchIncludingArchived.IsSuccess);
        Assert.Contains(searchIncludingArchived.Value!.Results, r => r.Id == noteId);

        // 7. Archiving an already-archived note is idempotent:
        // success, no second audit entry, and no version token rotation
        var auditsBeforeSecondArchive = await repo.GetAuditEntriesAsync(OwnerId.Default.Value, noteId);
        Assert.True(auditsBeforeSecondArchive.IsSuccess);
        var auditCountBefore = auditsBeforeSecondArchive.Value!.Count;

        var secondArchiveResult = await archiveHandler.HandleAsync(new ArchiveNoteCommand(
            Id: noteId,
            ExpectedVersionToken: archivedToken));
        Assert.True(secondArchiveResult.IsSuccess);
        Assert.Equal("archived", secondArchiveResult.Value!.Status);
        Assert.Equal(archivedToken, secondArchiveResult.Value.VersionToken); // No rotation

        var auditsAfterSecondArchive = await repo.GetAuditEntriesAsync(OwnerId.Default.Value, noteId);
        Assert.True(auditsAfterSecondArchive.IsSuccess);
        Assert.Equal(auditCountBefore, auditsAfterSecondArchive.Value!.Count); // No second audit row
    }

    [Fact]
    public async Task EndToEndAuditTest_ExercisesAllFiveWriteOperations_RecordsExactlyOneAuditRowPerOpWithSummaries()
    {
        await using var provider = BuildServiceProvider(_sharedMongo.ConnectionString, _databaseName);
        var repo = provider.GetRequiredService<INoteRepository>();
        var createHandler = provider.GetRequiredService<ICommandHandler<CreateNoteCommand, NoteCreatedDto>>();
        var appendHandler = provider.GetRequiredService<ICommandHandler<AppendToNoteCommand, NoteUpdatedDto>>();
        var replaceHandler = provider.GetRequiredService<ICommandHandler<ReplaceNoteSectionCommand, NoteSectionReplacedDto>>();
        var archiveHandler = provider.GetRequiredService<ICommandHandler<ArchiveNoteCommand, NoteArchivedDto>>();
        var importHandler = provider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();

        var owner = OwnerId.Default.Value;

        // Op 1: CreateNote
        var createOutcome = await createHandler.HandleAsync(new CreateNoteCommand(
            Title: "Audit E2E Note",
            Body: "# Audit E2E\n## Summary\nInitial line.\n",
            Area: "audit",
            Tags: ["audit", "e2e"],
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: "test"));
        Assert.True(createOutcome.IsSuccess);
        var noteId = createOutcome.Value!.Id;

        // Op 2: AppendToNote
        var appendOutcome = await appendHandler.HandleAsync(new AppendToNoteCommand(
            Id: noteId,
            TextToAppend: "- Appended audit point.\n"));
        Assert.True(appendOutcome.IsSuccess);

        // Op 3: ReplaceNoteSection (destructive edit: must produce revision reference)
        var replaceOutcome = await replaceHandler.HandleAsync(new ReplaceNoteSectionCommand(
            Id: noteId,
            Heading: "Summary",
            NewSectionBody: "## Summary\nReplaced line with new content.\n",
            VersionToken: appendOutcome.Value!.VersionToken));
        Assert.True(replaceOutcome.IsSuccess);

        // Op 4: ArchiveNote
        var archiveOutcome = await archiveHandler.HandleAsync(new ArchiveNoteCommand(
            Id: noteId,
            ExpectedVersionToken: replaceOutcome.Value!.VersionToken));
        Assert.True(archiveOutcome.IsSuccess);

        // Op 5: ImportMarkdown (import a new section into the same owner)
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "# Imported Audit Section\nImport body content.\n");
            var importOutcome = await importHandler.HandleAsync(new ImportMarkdownCommand(
                FilePaths: [tempFile],
                Area: "audit",
                Tags: ["imported"],
                DefaultDate: "2026-10-17",
                Actor: "cli-import",
                OwnerId: owner));
            Assert.True(importOutcome.IsSuccess);
            Assert.Equal(1, importOutcome.Value!.CreatedCount);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }

        // Inspect audit log for noteId: exactly 4 audit entries (create, append, replace, archive)
        var noteAudits = await repo.GetAuditEntriesAsync(owner, noteId);
        Assert.True(noteAudits.IsSuccess);
        Assert.Equal(4, noteAudits.Value!.Count);

        var a1 = noteAudits.Value[0];
        Assert.Equal(AuditOperation.NoteCreate, a1.Operation);
        Assert.Null(a1.BeforeSummary);
        Assert.NotNull(a1.AfterSummary);
        Assert.Null(a1.NoteRevisionId);

        var a2 = noteAudits.Value[1];
        Assert.Equal(AuditOperation.NoteAppend, a2.Operation);
        Assert.NotNull(a2.BeforeSummary);
        Assert.NotNull(a2.AfterSummary);
        Assert.Equal("- Appended audit point.\n", a2.AppendedText);
        Assert.Null(a2.NoteRevisionId);

        var a3 = noteAudits.Value[2];
        Assert.Equal(AuditOperation.NoteReplaceSection, a3.Operation);
        Assert.NotNull(a3.BeforeSummary);
        Assert.NotNull(a3.AfterSummary);
        Assert.NotNull(a3.NoteRevisionId); // Destructive edit references revision
        Assert.Equal(replaceOutcome.Value!.RevisionId, a3.NoteRevisionId.Value);

        var a4 = noteAudits.Value[3];
        Assert.Equal(AuditOperation.NoteArchive, a4.Operation);
        Assert.NotNull(a4.BeforeSummary);
        Assert.NotNull(a4.AfterSummary);
        Assert.Null(a4.NoteRevisionId);

        // All audits for owner: exactly 5 (4 for noteId + 1 import_create)
        var allAudits = await repo.GetAuditEntriesAsync(owner, null);
        Assert.True(allAudits.IsSuccess);
        Assert.Equal(5, allAudits.Value!.Count);
        var importAudit = allAudits.Value.First(a => a.Operation == AuditOperation.ImportCreate);
        Assert.NotNull(importAudit.AfterSummary);
        Assert.Equal("2026-10-17", importAudit.ResolvedDefaultDate);
    }
}
