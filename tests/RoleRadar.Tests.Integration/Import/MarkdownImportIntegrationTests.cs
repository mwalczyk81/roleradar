using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Import.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;
using RoleRadar.Infrastructure;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Import;

public sealed class MarkdownImportIntegrationTests : IAsyncLifetime
{
    private readonly SharedMongoFixture _sharedMongo;
    private readonly string _databaseName;
    private MongoClient? _client;

    public MarkdownImportIntegrationTests(SharedMongoFixture sharedMongo)
    {
        _sharedMongo = sharedMongo;
        _databaseName = $"roleradar_import_{Guid.CreateVersion7():N}";
    }

    public ValueTask InitializeAsync()
    {
        _client = new MongoClient(_sharedMongo.ConnectionString);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client != null)
            {
                await _client.DropDatabaseAsync(_databaseName);
            }
        }
        finally
        {
            _client?.Dispose();
        }
    }

    private ServiceProvider BuildServiceProvider(string? dbNameOverride = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddRoleRadarApplication();
        services.AddRoleRadarInfrastructure(opts =>
        {
            opts.ConnectionString = _sharedMongo.ConnectionString;
            opts.DatabaseName = dbNameOverride ?? _databaseName;
            opts.ReadinessTimeout = TimeSpan.FromSeconds(5);
        });
        return services.BuildServiceProvider();
    }

    private static string ResolveFixturePath(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "../../../../../tests/fixtures", fileName);
        if (File.Exists(fixturePath))
        {
            return Path.GetFullPath(fixturePath);
        }

        var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "tests/fixtures", fileName);
        if (File.Exists(cwdPath))
        {
            return Path.GetFullPath(cwdPath);
        }

        throw new FileNotFoundException($"Test fixture '{fileName}' not found at '{fixturePath}' or '{cwdPath}'.");
    }

    [Fact]
    public async Task Import_BothFixtures_PersistsNotesWithByteExactRoundTripReassembly()
    {
        await using var provider = BuildServiceProvider();
        var handler = provider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();
        var repo = provider.GetRequiredService<INoteRepository>();

        var primaryPath = ResolveFixturePath("fictional-memory-primary.md");
        var archivePath = ResolveFixturePath("fictional-memory-archive.md");

        var expectedPrimaryBytes = await File.ReadAllBytesAsync(primaryPath);
        var expectedArchiveBytes = await File.ReadAllBytesAsync(archivePath);

        var command = new ImportMarkdownCommand(
            FilePaths: [primaryPath, archivePath],
            Area: "criteria",
            Tags: ["test-tag"],
            DefaultDate: "2026-01-01",
            Actor: "integration-test");

        var outcome = await handler.HandleAsync(command);

        Assert.True(outcome.IsSuccess);
        var summary = outcome.Value!;
        Assert.Equal(2, summary.FilesProcessed);
        Assert.True(summary.CreatedCount > 0);
        Assert.Equal(0, summary.UpdatedCount);
        Assert.Equal(0, summary.UnchangedCount);
        Assert.Equal(0, summary.ConflictCount);
        Assert.Equal(0, summary.OrphanCount);

        // Verify primary fixture byte-exact round-trip reassembly
        var primaryNotesResult = await repo.GetImportedBySourceFileAsync(OwnerId.Default.Value, Path.GetFileName(primaryPath));
        Assert.True(primaryNotesResult.IsSuccess);
        var primaryNotes = primaryNotesResult.Value!.OrderBy(n => n.ImportMetadata!.SectionOrderIndex).ToList();
        var reassembledPrimary = string.Concat(primaryNotes.Select(n => n.Body));
        var expectedPrimary = Encoding.UTF8.GetString(expectedPrimaryBytes);
        Assert.Equal(expectedPrimary, reassembledPrimary);

        // Verify archive fixture byte-exact round-trip reassembly
        var archiveNotesResult = await repo.GetImportedBySourceFileAsync(OwnerId.Default.Value, Path.GetFileName(archivePath));
        Assert.True(archiveNotesResult.IsSuccess);
        var archiveNotes = archiveNotesResult.Value!.OrderBy(n => n.ImportMetadata!.SectionOrderIndex).ToList();
        var reassembledArchive = string.Concat(archiveNotes.Select(n => n.Body));
        var expectedArchive = Encoding.UTF8.GetString(expectedArchiveBytes);
        Assert.Equal(expectedArchive, reassembledArchive);
    }

    [Fact]
    public async Task Import_RerunWithDifferentDefaultDate_ProducesZeroDuplicatesAndZeroNewRevisions()
    {
        await using var provider = BuildServiceProvider();
        var handler = provider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();
        var repo = provider.GetRequiredService<INoteRepository>();

        var primaryPath = ResolveFixturePath("fictional-memory-primary.md");

        var run1 = await handler.HandleAsync(new ImportMarkdownCommand(
            FilePaths: [primaryPath],
            DefaultDate: "2026-01-01"));
        Assert.True(run1.IsSuccess);
        var initialCreated = run1.Value!.CreatedCount;

        // Re-run with a different default date
        var run2 = await handler.HandleAsync(new ImportMarkdownCommand(
            FilePaths: [primaryPath],
            DefaultDate: "2026-12-31"));
        Assert.True(run2.IsSuccess);

        var summary2 = run2.Value!;
        Assert.Equal(0, summary2.CreatedCount);
        Assert.Equal(0, summary2.UpdatedCount);
        Assert.Equal(initialCreated, summary2.UnchangedCount);
        Assert.Equal(0, summary2.ConflictCount);
        Assert.Equal(0, summary2.OrphanCount);

        var notesResult = await repo.GetImportedBySourceFileAsync(OwnerId.Default.Value, Path.GetFileName(primaryPath));
        Assert.True(notesResult.IsSuccess);
        Assert.Equal(initialCreated, notesResult.Value!.Count);

        // Verify no revisions were created
        foreach (var note in notesResult.Value!)
        {
            var revs = await repo.GetRevisionsAsync(OwnerId.Default.Value, note.Id);
            Assert.True(revs.IsSuccess);
            Assert.Empty(revs.Value!);
        }
    }

    [Fact]
    public async Task Import_WhenSourceSectionUpdatedAndStoredNoteUnmodified_UpdatesNoteAndCreatesRevision()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-update-{Guid.CreateVersion7():N}.md");
        try
        {
            await File.WriteAllTextAsync(tempFile, "# Header One\r\nInitial content.\r\n## Section Two\r\nSection two text.\r\n");

            await using var provider = BuildServiceProvider();
            var handler = provider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();
            var repo = provider.GetRequiredService<INoteRepository>();

            var initialImport = await handler.HandleAsync(new ImportMarkdownCommand(FilePaths: [tempFile]));
            Assert.True(initialImport.IsSuccess);
            Assert.Equal(2, initialImport.Value!.CreatedCount);

            // Update Header One in the source markdown file
            await File.WriteAllTextAsync(tempFile, "# Header One\r\nUpdated source content.\r\n## Section Two\r\nSection two text.\r\n");

            var secondImport = await handler.HandleAsync(new ImportMarkdownCommand(FilePaths: [tempFile]));
            Assert.True(secondImport.IsSuccess);

            var summary2 = secondImport.Value!;
            Assert.Equal(0, summary2.CreatedCount);
            Assert.Equal(1, summary2.UpdatedCount);
            Assert.Equal(1, summary2.UnchangedCount);
            Assert.Equal(0, summary2.ConflictCount);
            Assert.Equal(0, summary2.OrphanCount);

            var notes = (await repo.GetImportedBySourceFileAsync(OwnerId.Default.Value, Path.GetFileName(tempFile))).Value!;
            var headerOneNote = notes.First(n => n.ImportMetadata?.HeadingPath == "# Header One");
            Assert.Contains("Updated source content.", headerOneNote.Body);
            Assert.Equal(1, headerOneNote.RevisionCount);

            var revisions = (await repo.GetRevisionsAsync(OwnerId.Default.Value, headerOneNote.Id)).Value!;
            Assert.Single(revisions);
            Assert.Equal(NoteRevision.ReasonImportOverwrite, revisions[0].Reason);
            Assert.Contains("Initial content.", revisions[0].PriorBody);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task Import_WhenNoteEditedAfterImport_SkipsAndReportsConflictWithoutOverwriting()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-conflict-{Guid.CreateVersion7():N}.md");
        try
        {
            await File.WriteAllTextAsync(tempFile, "# Stable Heading\r\nOriginal text.\r\n");

            await using var provider = BuildServiceProvider();
            var handler = provider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();
            var repo = provider.GetRequiredService<INoteRepository>();

            var initialImport = await handler.HandleAsync(new ImportMarkdownCommand(FilePaths: [tempFile]));
            Assert.True(initialImport.IsSuccess);

            var notes = (await repo.GetImportedBySourceFileAsync(OwnerId.Default.Value, Path.GetFileName(tempFile))).Value!;
            var note = notes.Single();

            // Simulate agent/user edit after import: append to the note in DB
            var appendOutcome = await repo.ExecuteInTransactionAsync((tx, ct) =>
                tx.AppendAsync(OwnerId.Default.Value, note.Id, "\nAppended line by user.", "agent", DateTime.UtcNow, ct));
            Assert.True(appendOutcome.IsSuccess);

            // Now update the source file as well
            await File.WriteAllTextAsync(tempFile, "# Stable Heading\r\nSource changed text.\r\n");

            var reimport = await handler.HandleAsync(new ImportMarkdownCommand(FilePaths: [tempFile]));
            Assert.True(reimport.IsSuccess);

            var summary = reimport.Value!;
            Assert.Equal(0, summary.CreatedCount);
            Assert.Equal(0, summary.UpdatedCount);
            Assert.Equal(1, summary.ConflictCount);
            Assert.Single(summary.Conflicts);
            Assert.Equal(note.Id, summary.Conflicts[0].NoteId);

            // Verify stored note was NOT overwritten
            var currentNote = (await repo.GetByIdAsync(OwnerId.Default.Value, note.Id)).Value!;
            Assert.Contains("Appended line by user.", currentNote.Body);
            Assert.DoesNotContain("Source changed text.", currentNote.Body);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task Import_WhenHeadingRenamedOrRemovedInSource_ReportsOrphanAndNeverDeletes()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-orphan-{Guid.CreateVersion7():N}.md");
        try
        {
            await File.WriteAllTextAsync(tempFile, "# Section Alpha\r\nAlpha text.\r\n## Section Beta\r\nBeta text.\r\n");

            await using var provider = BuildServiceProvider();
            var handler = provider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();
            var repo = provider.GetRequiredService<INoteRepository>();

            var initialImport = await handler.HandleAsync(new ImportMarkdownCommand(FilePaths: [tempFile]));
            Assert.True(initialImport.IsSuccess);
            Assert.Equal(2, initialImport.Value!.CreatedCount);

            // Source file is updated: Section Beta was renamed to Section Gamma
            await File.WriteAllTextAsync(tempFile, "# Section Alpha\r\nAlpha text.\r\n## Section Gamma\r\nGamma text.\r\n");

            var reimport = await handler.HandleAsync(new ImportMarkdownCommand(FilePaths: [tempFile]));
            Assert.True(reimport.IsSuccess);

            var summary = reimport.Value!;
            Assert.Equal(1, summary.CreatedCount); // Section Gamma created
            Assert.Equal(1, summary.UnchangedCount); // Section Alpha unchanged
            Assert.Equal(1, summary.OrphanCount); // Section Beta is orphaned!
            Assert.Single(summary.Orphans);
            Assert.Equal("Section Beta", summary.Orphans[0].Title);

            // Verify that Section Beta is STILL in the database (never deleted)
            var allNotes = (await repo.GetImportedBySourceFileAsync(OwnerId.Default.Value, Path.GetFileName(tempFile))).Value!;
            Assert.Equal(3, allNotes.Count);
            Assert.Contains(allNotes, n => n.Title == "Section Beta");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task Import_InvalidUtf8File_RejectedBeforeAnyDatabaseWrite()
    {
        var invalidTempFile = Path.Combine(Path.GetTempPath(), $"test-invalid-utf8-{Guid.CreateVersion7():N}.md");
        try
        {
            byte[] invalidBytes = [0xFF, 0xFE, 0x41, 0x42];
            await File.WriteAllBytesAsync(invalidTempFile, invalidBytes);

            await using var provider = BuildServiceProvider();
            var handler = provider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();
            var repo = provider.GetRequiredService<INoteRepository>();

            var outcome = await handler.HandleAsync(new ImportMarkdownCommand(FilePaths: [invalidTempFile]));

            Assert.False(outcome.IsSuccess);
            Assert.Equal(ErrorCode.InvalidUtf8, outcome.Error!.Value.Code);

            // Verify no notes or audit entries were written
            var notes = (await repo.GetImportedBySourceFileAsync(OwnerId.Default.Value, Path.GetFileName(invalidTempFile))).Value!;
            Assert.Empty(notes);

            var auditEntries = (await repo.GetAuditEntriesAsync(OwnerId.Default.Value)).Value!;
            Assert.Empty(auditEntries);
        }
        finally
        {
            if (File.Exists(invalidTempFile))
            {
                File.Delete(invalidTempFile);
            }
        }
    }

    [Fact]
    public async Task Import_DryRun_RunsReconciliationWithZeroDatabaseWritesAndZeroAuditEntries()
    {
        await using var provider = BuildServiceProvider();
        var handler = provider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();
        var repo = provider.GetRequiredService<INoteRepository>();

        var primaryPath = ResolveFixturePath("fictional-memory-primary.md");

        var dryRunCommand = new ImportMarkdownCommand(
            FilePaths: [primaryPath],
            DryRun: true,
            DefaultDate: "2026-01-01");

        var dryRunOutcome = await handler.HandleAsync(dryRunCommand);

        Assert.True(dryRunOutcome.IsSuccess);
        var dryRunSummary = dryRunOutcome.Value!;
        Assert.True(dryRunSummary.CreatedCount > 0);
        Assert.Equal(0, dryRunSummary.UpdatedCount);
        Assert.Equal(0, dryRunSummary.ConflictCount);
        Assert.Equal(0, dryRunSummary.OrphanCount);

        // Verify zero notes and zero audit entries were written
        var notesResult = await repo.GetImportedBySourceFileAsync(OwnerId.Default.Value, Path.GetFileName(primaryPath));
        Assert.True(notesResult.IsSuccess);
        Assert.Empty(notesResult.Value!);

        var auditEntries = (await repo.GetAuditEntriesAsync(OwnerId.Default.Value)).Value!;
        Assert.Empty(auditEntries);

        // Verify a subsequent real import produces the exact same counts
        var realCommand = new ImportMarkdownCommand(
            FilePaths: [primaryPath],
            DryRun: false,
            DefaultDate: "2026-01-01");

        var realOutcome = await handler.HandleAsync(realCommand);
        Assert.True(realOutcome.IsSuccess);
        var realSummary = realOutcome.Value!;

        Assert.Equal(dryRunSummary.CreatedCount, realSummary.CreatedCount);
        Assert.Equal(dryRunSummary.UpdatedCount, realSummary.UpdatedCount);
        Assert.Equal(dryRunSummary.UnchangedCount, realSummary.UnchangedCount);
        Assert.Equal(dryRunSummary.ConflictCount, realSummary.ConflictCount);
        Assert.Equal(dryRunSummary.OrphanCount, realSummary.OrphanCount);
    }

    [Fact]
    public async Task Import_WhenFilePathsCollideOnFileNameCaseInsensitive_FailsValidationAndWritesZeroRecords()
    {
        var tempDirA = Path.Combine(Path.GetTempPath(), $"dirA_{Guid.CreateVersion7():N}");
        var tempDirB = Path.Combine(Path.GetTempPath(), $"dirB_{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(tempDirA);
        Directory.CreateDirectory(tempDirB);

        var pathA = Path.Combine(tempDirA, "collide.md");
        var pathB = Path.Combine(tempDirB, "COLLIDE.MD");

        try
        {
            await File.WriteAllTextAsync(pathA, "# Heading A\nContent A\n");
            await File.WriteAllTextAsync(pathB, "# Heading B\nContent B\n");

            await using var provider = BuildServiceProvider();
            var handler = provider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();
            var repo = provider.GetRequiredService<INoteRepository>();

            var outcome = await handler.HandleAsync(new ImportMarkdownCommand(FilePaths: [pathA, pathB]));

            Assert.False(outcome.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, outcome.Error!.Value.Code);
            Assert.Contains(pathA, outcome.Error.Value.Message, StringComparison.Ordinal);
            Assert.Contains(pathB, outcome.Error.Value.Message, StringComparison.Ordinal);

            // Verify zero notes and zero audit entries were written
            var notesA = (await repo.GetImportedBySourceFileAsync(OwnerId.Default.Value, "collide.md")).Value!;
            Assert.Empty(notesA);

            var auditEntries = (await repo.GetAuditEntriesAsync(OwnerId.Default.Value)).Value!;
            Assert.Empty(auditEntries);
        }
        finally
        {
            if (Directory.Exists(tempDirA))
            {
                Directory.Delete(tempDirA, true);
            }

            if (Directory.Exists(tempDirB))
            {
                Directory.Delete(tempDirB, true);
            }
        }
    }

    [Fact]
    public async Task Import_WhenSingleFileWithSourceId_UsesOverriddenSourceFileId()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-source-id-{Guid.CreateVersion7():N}.md");
        const string customSourceId = "overridden-source-id.md";

        try
        {
            await File.WriteAllTextAsync(tempFile, "# Section One\r\nContent one.\r\n## Section Two\r\nContent two.\r\n");

            await using var provider = BuildServiceProvider();
            var handler = provider.GetRequiredService<ICommandHandler<ImportMarkdownCommand, ImportMarkdownSummaryDto>>();
            var repo = provider.GetRequiredService<INoteRepository>();

            var outcome = await handler.HandleAsync(new ImportMarkdownCommand(
                FilePaths: [tempFile],
                SourceFileId: customSourceId));

            Assert.True(outcome.IsSuccess);
            Assert.Equal(2, outcome.Value!.CreatedCount);

            // Verify notes are stored under the overridden source ID, not the temp file name
            var notesByCustomId = (await repo.GetImportedBySourceFileAsync(OwnerId.Default.Value, customSourceId)).Value!;
            Assert.Equal(2, notesByCustomId.Count);
            Assert.All(notesByCustomId, n =>
            {
                Assert.Equal(customSourceId, n.ImportMetadata!.SourceFileId);
                Assert.StartsWith($"{customSourceId}#", n.ImportMetadata.ImportKey, StringComparison.Ordinal);
            });

            var notesByFileName = (await repo.GetImportedBySourceFileAsync(OwnerId.Default.Value, Path.GetFileName(tempFile))).Value!;
            Assert.Empty(notesByFileName);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
