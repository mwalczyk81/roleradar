using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;
using Xunit;

namespace RoleRadar.Tests.Unit.Domain;

public sealed class NoteAndAuditDomainTests
{
    [Fact]
    public void Sha256Hasher_ComputesLowercaseHexSha256OfUtf8Bytes()
    {
        var emptyHash = Sha256Hasher.ComputeLowerHex(string.Empty);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", emptyHash);

        var crlfHash = Sha256Hasher.ComputeLowerHex("Line 1\r\nLine 2\r\n");
        var lfHash = Sha256Hasher.ComputeLowerHex("Line 1\nLine 2\n");
        Assert.NotEqual(crlfHash, lfHash);
        Assert.Equal(64, crlfHash.Length);
        Assert.Equal(crlfHash.ToLowerInvariant(), crlfHash);
    }

    [Fact]
    public void OwnerId_DefaultsToLocalOwner_AndRejectsBlank()
    {
        Assert.Equal("local-owner", OwnerId.Default.Value);
        Assert.Throws<ArgumentException>(() => new OwnerId("   "));
    }

    [Fact]
    public void ProvenanceMarker_ValidatesKindAndIsoDate_AndNormalizesQualifier()
    {
        var marker = new ProvenanceMarker(ProvenanceKind.Stated, "2026-10-17", "  from career retro  ");
        Assert.Equal(ProvenanceKind.Stated, marker.Kind);
        Assert.Equal("2026-10-17", marker.Date);
        Assert.Equal("from career retro", marker.Qualifier);

        Assert.Throws<ArgumentException>(() => new ProvenanceMarker(ProvenanceKind.Inferred, "10/17/2026"));
        Assert.Throws<ArgumentException>(() => new ProvenanceMarker(ProvenanceKind.Researched, "2026-02-30"));
    }

    [Fact]
    public void Note_Create_NormalizesTagsAndArea_UsesUuidV7_AndPreservesExactBodyBytes()
    {
        var now = new DateTime(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc);
        var bodyWithBomAndCrlf = "\uFEFF# Fictional Criteria\r\n- Remote first\r\n";

        var note = Note.Create(
            ownerId: OwnerId.Default.Value,
            title: "  Target Role Criteria  ",
            body: bodyWithBomAndCrlf,
            tags: ["  Remote ", "DOTNET", "remote", ""],
            area: "  CRITERIA ",
            provenance: new ProvenanceMarker(ProvenanceKind.Stated, "2026-10-17"),
            createdAtUtc: now);

        Assert.NotEqual(Guid.Empty, note.Id);
        Assert.NotEqual(Guid.Empty, note.VersionToken);
        Assert.Equal("local-owner", note.OwnerId);
        Assert.Equal("Target Role Criteria", note.Title);
        Assert.Equal(bodyWithBomAndCrlf, note.Body);
        Assert.Equal(Sha256Hasher.ComputeLowerHex(bodyWithBomAndCrlf), note.CurrentBodyHash);
        Assert.Equal(["remote", "dotnet"], note.Tags);
        Assert.Equal("criteria", note.Area);
        Assert.Equal(NoteStatus.Active, note.Status);
        Assert.Equal(0, note.RevisionCount);
        Assert.Null(note.ImportMetadata);
        Assert.Equal(now, note.CreatedAtUtc);
        Assert.Equal(now, note.UpdatedAtUtc);
    }

    [Fact]
    public void Note_AppendBody_UpdatesBodyAndRotatesVersionToken_WithoutIncrementingRevisionCount()
    {
        var t0 = new DateTime(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddMinutes(5);
        var note = Note.Create(
            ownerId: OwnerId.Default.Value,
            title: "Runbook",
            body: "## Section 1\nInitial line\n",
            tags: ["runbook"],
            area: "runbook",
            provenance: new ProvenanceMarker(ProvenanceKind.Stated, "2026-10-17"),
            createdAtUtc: t0);

        var originalVersionToken = note.VersionToken;
        note.AppendBody("Appended line\n", t1);

        Assert.Equal("## Section 1\nInitial line\nAppended line\n", note.Body);
        Assert.Equal(Sha256Hasher.ComputeLowerHex(note.Body), note.CurrentBodyHash);
        Assert.NotEqual(originalVersionToken, note.VersionToken);
        Assert.Equal(0, note.RevisionCount);
        Assert.Equal(t1, note.UpdatedAtUtc);
    }

    [Fact]
    public void Note_ReplaceBodyWithRevision_CreatesFullPriorBodyRevision_AndIncrementsRevisionCount()
    {
        var t0 = new DateTime(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddMinutes(10);
        var priorBody = "# Heading\r\nPrior body text\r\n";
        var note = Note.Create(
            ownerId: OwnerId.Default.Value,
            title: "Heading",
            body: priorBody,
            tags: [],
            area: "general",
            provenance: new ProvenanceMarker(ProvenanceKind.Researched, "2026-10-17", "blog"),
            createdAtUtc: t0);

        var priorToken = note.VersionToken;
        var newBody = "# Heading\r\nReplaced body text\r\n";

        var revision = note.ReplaceBodyWithRevision(
            newBody: newBody,
            reason: NoteRevision.ReasonReplaceSection,
            occurredAtUtc: t1);

        Assert.Equal(1, note.RevisionCount);
        Assert.Equal(newBody, note.Body);
        Assert.Equal(Sha256Hasher.ComputeLowerHex(newBody), note.CurrentBodyHash);
        Assert.NotEqual(priorToken, note.VersionToken);
        Assert.Equal(t1, note.UpdatedAtUtc);

        Assert.Equal(note.Id, revision.NoteId);
        Assert.Equal(note.OwnerId, revision.OwnerId);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal("replace-section", revision.Reason);
        Assert.Equal(priorBody, revision.PriorBody);
        Assert.Equal(Sha256Hasher.ComputeLowerHex(priorBody), revision.PriorBodyHash);
        Assert.Equal(priorToken, revision.PriorVersionToken);
        Assert.Equal(t1, revision.CreatedAtUtc);
    }

    [Fact]
    public void NoteImportMetadata_ComputesCompositeKey_AndDetectsPostImportEditsByBodyHashOnly()
    {
        var t0 = new DateTime(2026, 10, 17, 14, 0, 0, DateTimeKind.Utc);
        var body = "## Loop Notes\r\nFictional interview loop.\r\n";
        var bodyHash = Sha256Hasher.ComputeLowerHex(body);
        var importMeta = NoteImportMetadata.Create(
            sourceFileId: "fictional-memory-primary.md",
            headingPath: "# Strategy / ## Loop Notes",
            occurrenceIndex: 1,
            sectionOrderIndex: 4,
            lastImportedBodyHash: bodyHash);

        Assert.Equal("fictional-memory-primary.md## Strategy / ## Loop Notes#1", importMeta.ImportKey);

        var note = Note.CreateImported(
            ownerId: OwnerId.Default.Value,
            title: "Loop Notes",
            body: body,
            tags: ["imported"],
            area: "criteria",
            provenance: new ProvenanceMarker(ProvenanceKind.Inferred, "2026-10-17"),
            importMetadata: importMeta,
            createdAtUtc: t0);

        Assert.False(note.HasPostImportEdits);

        // Changing structured provenance date does NOT mark note as edited since import
        note.UpdateProvenance(new ProvenanceMarker(ProvenanceKind.Inferred, "2026-10-18"), t0.AddMinutes(1));
        Assert.False(note.HasPostImportEdits);

        // Appending or changing body changes CurrentBodyHash != LastImportedBodyHash
        note.AppendBody("Post-import agent edit\r\n", t0.AddMinutes(2));
        Assert.True(note.HasPostImportEdits);
    }

    [Fact]
    public void AuditLogEntry_EnforcesOperationInvariants_ForRevisionIdAndAppendedText()
    {
        var now = new DateTime(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc);
        var noteId = Guid.CreateVersion7();
        var versionToken = Guid.CreateVersion7();
        var beforeSummary = new AuditContentSummary(Sha256Hasher.ComputeLowerHex("old"), 3, "active", "old");
        var afterSummary = new AuditContentSummary(Sha256Hasher.ComputeLowerHex("old+new"), 7, "active", "old+new");

        // Append MUST carry appended_text and MUST NOT carry note_revision_id
        var appendAudit = AuditLogEntry.CreateForAppend(
            ownerId: OwnerId.Default.Value,
            actor: "mcp-stdio",
            targetNoteId: noteId,
            appendedText: "+new",
            resultingVersionToken: versionToken,
            occurredAtUtc: now,
            beforeSummary: beforeSummary,
            afterSummary: afterSummary);

        Assert.Equal(AuditOperation.NoteAppend, appendAudit.Operation);
        Assert.Null(appendAudit.NoteRevisionId);
        Assert.Equal("+new", appendAudit.AppendedText);

        // Replace-section MUST carry note_revision_id and MUST NOT carry appended_text
        var revisionId = Guid.CreateVersion7();
        var replaceAudit = AuditLogEntry.CreateForReplaceSection(
            ownerId: OwnerId.Default.Value,
            actor: "mcp-stdio",
            targetNoteId: noteId,
            noteRevisionId: revisionId,
            resultingVersionToken: versionToken,
            occurredAtUtc: now,
            beforeSummary: beforeSummary,
            afterSummary: afterSummary);

        Assert.Equal(AuditOperation.NoteReplaceSection, replaceAudit.Operation);
        Assert.Equal(revisionId, replaceAudit.NoteRevisionId);
        Assert.Null(replaceAudit.AppendedText);
    }

    [Fact]
    public void Result_RepresentsTypedSuccessAndFailureWithoutExceptions()
    {
        var ok = Result<int>.Success(42);
        Assert.True(ok.IsSuccess);
        Assert.Equal(42, ok.Value);
        Assert.Null(ok.Error);

        var err = Result<int>.Failure(new ResultError(ErrorCode.StaleVersionToken, "Version mismatch"));
        Assert.False(err.IsSuccess);
        Assert.NotNull(err.Error);
        Assert.Equal(ErrorCode.StaleVersionToken, err.Error.Value.Code);
    }
}
