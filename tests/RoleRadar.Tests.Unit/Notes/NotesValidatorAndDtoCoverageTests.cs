using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Import.Commands;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Application.Notes.Queries;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Notes;
using Xunit;

namespace RoleRadar.Tests.Unit.Notes;

public sealed class NotesValidatorAndDtoCoverageTests
{
    [Fact]
    public void ArchiveNoteCommandValidator_ValidatesInput()
    {
        var validator = new ArchiveNoteCommandValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));

        // Id empty
        Assert.NotNull(validator.Validate(new ArchiveNoteCommand(Guid.Empty)));

        // ExpectedVersionToken empty
        Assert.NotNull(validator.Validate(new ArchiveNoteCommand(Guid.CreateVersion7(), ExpectedVersionToken: Guid.Empty)));

        // Valid
        Assert.Null(validator.Validate(new ArchiveNoteCommand(Guid.CreateVersion7())));
        Assert.Null(validator.Validate(new ArchiveNoteCommand(Guid.CreateVersion7(), ExpectedVersionToken: Guid.CreateVersion7())));
    }

    [Fact]
    public void ReplaceNoteSectionCommandValidator_ValidatesInput()
    {
        var validator = new ReplaceNoteSectionCommandValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));

        var id = Guid.CreateVersion7();
        var token = Guid.CreateVersion7();

        // Id empty
        Assert.NotNull(validator.Validate(new ReplaceNoteSectionCommand(Guid.Empty, "## Target", "Body", token)));

        // Heading empty
        Assert.NotNull(validator.Validate(new ReplaceNoteSectionCommand(id, "", "Body", token)));
        Assert.NotNull(validator.Validate(new ReplaceNoteSectionCommand(id, "   ", "Body", token)));

        // NewSectionBody null
        Assert.NotNull(validator.Validate(new ReplaceNoteSectionCommand(id, "## Target", null!, token)));

        // VersionToken empty
        Assert.NotNull(validator.Validate(new ReplaceNoteSectionCommand(id, "## Target", "Body", Guid.Empty)));

        // Valid
        Assert.Null(validator.Validate(new ReplaceNoteSectionCommand(id, "## Target", "Valid new body", token)));
    }

    [Fact]
    public void ImportMarkdownCommandValidator_ValidatesInput()
    {
        var validator = new ImportMarkdownCommandValidator();

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));

        // Empty file paths
        Assert.NotNull(validator.Validate(new ImportMarkdownCommand([])));

        // Null or whitespace file path
        Assert.NotNull(validator.Validate(new ImportMarkdownCommand([""])));

        // Non-existent file path
        Assert.NotNull(validator.Validate(new ImportMarkdownCommand(["C:\\non_existent_folder\\does_not_exist.md"])));

        // Colliding source filenames
        var tempFile1 = Path.GetTempFileName();
        var subDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(subDir);
        var tempFile2 = Path.Combine(subDir, Path.GetFileName(tempFile1));
        File.WriteAllText(tempFile1, "hello");
        File.WriteAllText(tempFile2, "world");

        try
        {
            var collidingRes = validator.Validate(new ImportMarkdownCommand([tempFile1, tempFile2]));
            Assert.NotNull(collidingRes);
            Assert.Contains("colliding", collidingRes.Value.Message);

            // SourceFileId with multiple files
            var otherTemp = Path.GetTempFileName();
            File.WriteAllText(otherTemp, "test");
            try
            {
                var multiSourceRes = validator.Validate(new ImportMarkdownCommand([tempFile1, otherTemp], SourceFileId: "custom-id"));
                Assert.NotNull(multiSourceRes);
                Assert.Contains("--source-id", multiSourceRes.Value.Message);
            }
            finally
            {
                File.Delete(otherTemp);
            }

            // Area empty or too long
            Assert.NotNull(validator.Validate(new ImportMarkdownCommand([tempFile1], Area: "")));
            Assert.NotNull(validator.Validate(new ImportMarkdownCommand([tempFile1], Area: new string('a', Note.MaxAreaChars + 1))));

            // Tags count too many
            var tooManyTags = Enumerable.Range(0, Note.MaxTagCount + 1).Select(i => $"tag{i}").ToList();
            Assert.NotNull(validator.Validate(new ImportMarkdownCommand([tempFile1], Tags: tooManyTags)));

            // Tag empty or too long
            Assert.NotNull(validator.Validate(new ImportMarkdownCommand([tempFile1], Tags: [""])));
            Assert.NotNull(validator.Validate(new ImportMarkdownCommand([tempFile1], Tags: [new string('t', Note.MaxTagChars + 1)])));

            // Invalid default date
            Assert.NotNull(validator.Validate(new ImportMarkdownCommand([tempFile1], DefaultDate: "bad-date")));

            // Valid
            Assert.Null(validator.Validate(new ImportMarkdownCommand([tempFile1], Area: "imported", Tags: ["tag1"], DefaultDate: "2026-10-18")));
        }
        finally
        {
            File.Delete(tempFile1);
            File.Delete(tempFile2);
            Directory.Delete(subDir, true);
        }
    }

    [Fact]
    public void NoteStatusExtensions_And_ProvenanceKindExtensions_TestAllBranches()
    {
        // NoteStatusExtensions
        Assert.Throws<ArgumentOutOfRangeException>(() => ((NoteStatus)999).ToWireString());
        Assert.Throws<ArgumentException>(() => NoteStatusExtensions.FromWireString(""));
        Assert.Throws<ArgumentException>(() => NoteStatusExtensions.FromWireString("unknown"));
        Assert.Equal(NoteStatus.Active, NoteStatusExtensions.FromWireString("active"));
        Assert.Equal(NoteStatus.Archived, NoteStatusExtensions.FromWireString("archived"));

        // ProvenanceKindExtensions
        Assert.Throws<ArgumentOutOfRangeException>(() => ((ProvenanceKind)999).ToWireString());
        Assert.Throws<ArgumentException>(() => ProvenanceKindExtensions.FromWireString(""));
        Assert.Throws<ArgumentException>(() => ProvenanceKindExtensions.FromWireString("invalid"));
        Assert.Equal(ProvenanceKind.Stated, ProvenanceKindExtensions.FromWireString("stated"));
        Assert.Equal(ProvenanceKind.Researched, ProvenanceKindExtensions.FromWireString("researched"));
        Assert.Equal(ProvenanceKind.Inferred, ProvenanceKindExtensions.FromWireString("inferred"));

        Assert.False(ProvenanceKindExtensions.TryParseWireString(null, out _));
        Assert.False(ProvenanceKindExtensions.TryParseWireString("unknown", out _));
        Assert.True(ProvenanceKindExtensions.TryParseWireString("stated", out var p1) && p1 == ProvenanceKind.Stated);
        Assert.True(ProvenanceKindExtensions.TryParseWireString("researched", out var p2) && p2 == ProvenanceKind.Researched);
        Assert.True(ProvenanceKindExtensions.TryParseWireString("inferred", out var p3) && p3 == ProvenanceKind.Inferred);
    }

    [Fact]
    public void NotesDtos_And_Abstractions_ExerciseAllPropertiesAndEquality()
    {
        var id = Guid.CreateVersion7();
        var token = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        // NoteSearchItemDto & NoteSearchResponseDto & SearchNotesQuery
        var provDto = new ProvenanceDto("stated", "2026-10-18", "retro");
        var searchItem = new NoteSearchItemDto(id, "Title", "Area", ["tag1"], provDto, "active", token, "snippet text", 0.95, now);
        Assert.Equal(id, searchItem.Id);
        Assert.Equal("Title", searchItem.Title);
        Assert.Equal("Area", searchItem.Area);
        Assert.Single(searchItem.Tags);
        Assert.Equal(provDto, searchItem.Provenance);
        Assert.Equal("active", searchItem.Status);
        Assert.Equal(token, searchItem.VersionToken);
        Assert.Equal("snippet text", searchItem.Snippet);
        Assert.Equal(0.95, searchItem.Score);
        Assert.Equal(now, searchItem.UpdatedAtUtc);
        Assert.Equal(searchItem, searchItem with { });

        var searchResponse = new NoteSearchResponseDto([searchItem]);
        Assert.Single(searchResponse.Results);
        Assert.Equal(searchResponse, searchResponse with { });

        var searchQuery = new SearchNotesQuery("query", ["tag"], "Area", true, 50, "owner-1");
        Assert.Equal("query", searchQuery.Query);
        Assert.True(searchQuery.IncludeArchived);
        Assert.Equal(searchQuery, searchQuery with { });

        // NoteArchivedDto & NoteCreatedDto & NoteSectionReplacedDto & NoteUpdatedDto
        var archivedDto = new NoteArchivedDto(id, token, "archived", now);
        Assert.Equal(id, archivedDto.Id);
        Assert.Equal(archivedDto, archivedDto with { });

        var createdDto = new NoteCreatedDto(id, "Title", "Area", ["tag1"], provDto, "active", token, "bodyhash", now, now);
        Assert.Equal(id, createdDto.Id);
        Assert.Equal("Title", createdDto.Title);
        Assert.Equal(createdDto, createdDto with { });

        var replacedDto = new NoteSectionReplacedDto(id, token, Guid.CreateVersion7(), 2, "hash123", now);
        Assert.Equal(id, replacedDto.Id);
        Assert.Equal(replacedDto, replacedDto with { });

        var updatedDto = new NoteUpdatedDto(id, token, "bodyhash", 50, now);
        Assert.Equal(id, updatedDto.Id);
        Assert.Equal(50, updatedDto.ByteLength);
        Assert.Equal(updatedDto, updatedDto with { });

        // ImportMarkdownConflictDto, ImportMarkdownOrphanDto, ImportMarkdownSummaryDto
        var conflict = new ImportMarkdownConflictDto("key1", id, "Conflict Title", "Reason");
        Assert.Equal("key1", conflict.ImportKey);
        Assert.Equal(conflict, conflict with { });

        var orphan = new ImportMarkdownOrphanDto("key2", id, "Orphan Title");
        Assert.Equal("key2", orphan.ImportKey);
        Assert.Equal(orphan, orphan with { });

        var summary = new ImportMarkdownSummaryDto(10, 5, 2, 1, 1, 1, [conflict], [orphan]);
        Assert.Equal(10, summary.FilesProcessed);
        Assert.Equal(summary, summary with { });

        // BackupArchiveSummaryDto, RestoreArchiveSummaryDto, WipeSummaryDto
        var backup = new BackupArchiveSummaryDto("C:\\backup.zip", 1024, now);
        Assert.Equal("C:\\backup.zip", backup.ArchivePath);
        Assert.Equal(1024, backup.BytesWritten);
        Assert.Equal(now, backup.CreatedAtUtc);
        Assert.Equal(backup, backup with { });

        var restore = new RestoreArchiveSummaryDto("C:\\backup.zip", new Dictionary<string, long> { ["notes"] = 5 }, now);
        Assert.Equal("C:\\backup.zip", restore.ArchivePath);
        Assert.Equal(restore, restore with { });

        var wipe = new WipeSummaryDto(new Dictionary<string, long> { ["notes"] = 5 }, now);
        Assert.Single(wipe.DroppedCounts);
        Assert.Equal(wipe, wipe with { });

        // NoteWriteOutcome, StoreReadinessStatus, NoteSearchCriteria, NoteSearchHit
        var note = Note.Create(
            "owner-1", "Test Note", "body", ["t1"], "area",
            new ProvenanceMarker(ProvenanceKind.Stated, "2026-10-18"), now);
        var audit = AuditLogEntry.Create(
            "owner-1", "actor", AuditOperation.NoteCreate, id, null, null, token, null, now, null, AuditContentSummary.FromNote(note));
        var outcome = new NoteWriteOutcome(note, audit);
        Assert.Equal(note, outcome.Note);
        Assert.Equal(audit, outcome.AuditEntry);
        Assert.Null(outcome.Revision);
        Assert.Equal(outcome, outcome with { });

        var readiness = new StoreReadinessStatus(true, "localhost:27017", "rs0", 1.5, null);
        Assert.True(readiness.IsReady);
        Assert.Equal("localhost:27017", readiness.Endpoint);
        Assert.Equal(readiness, readiness with { });

        var criteria = new NoteSearchCriteria("owner-1", "query", ["tag"], "area", false, 20);
        Assert.Equal("owner-1", criteria.OwnerId);
        Assert.Equal(criteria, criteria with { });

        var hit = new NoteSearchHit(note, 0.88, "snippet");
        Assert.Equal(note, hit.Note);
        Assert.Equal(hit, hit with { });

        var upsertReq = new UpsertImportedSectionRequest(
            "owner-1", "src1", "Heading > Sub", 0, 1, "Title", "Body", ["tag"], "area",
            new ProvenanceMarker(ProvenanceKind.Stated, "2026-10-18"), "2026-10-18", "actor", now);
        Assert.Equal("owner-1", upsertReq.OwnerId);
        Assert.Equal("src1", upsertReq.SourceFileId);
        Assert.Equal(upsertReq, upsertReq with { });
    }
}
