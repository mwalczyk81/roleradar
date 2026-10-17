using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Application.Notes.Queries;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;
using Xunit;

namespace RoleRadar.Tests.Unit.Notes;

public sealed class NoteHandlersUnitTests
{
    private static readonly DateTime FixedNow = new(2026, 10, 17, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateNoteCommandHandler_ValidInput_PersistsNoteAndAudit_AndReturnsNoteCreatedDto()
    {
        var repo = new InMemoryFakeNoteRepository();
        var clock = new FixedUtcClock(FixedNow);
        var handler = new CreateNoteCommandHandler(repo, clock);

        var command = new CreateNoteCommand(
            Title: "Target Role Criteria",
            Body: "# Role Criteria\n## Compensation\n180k base.\n",
            Area: "Criteria",
            Tags: ["Remote", "DotNet"],
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: "initial setup",
            Actor: "unit-test");

        var result = await handler.HandleAsync(command);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("Target Role Criteria", result.Value.Title);
        Assert.Equal("criteria", result.Value.Area);
        Assert.Equal(["remote", "dotnet"], result.Value.Tags);
        Assert.Equal("stated", result.Value.Provenance.Kind);
        Assert.Equal("2026-10-17", result.Value.Provenance.Date);
        Assert.Equal("initial setup", result.Value.Provenance.Qualifier);
        Assert.Equal("active", result.Value.Status);
        Assert.Single(repo.Audits);
        Assert.Equal(AuditOperation.NoteCreate, repo.Audits[0].Operation);
    }

    [Fact]
    public async Task CreateNoteCommandHandler_InvalidInput_ReturnsValidationErrorWithoutThrowing()
    {
        var repo = new InMemoryFakeNoteRepository();
        var clock = new FixedUtcClock(FixedNow);
        var handler = new CreateNoteCommandHandler(repo, clock);

        var invalidDateCommand = new CreateNoteCommand(
            Title: "Valid Title",
            Body: "Body",
            Area: "criteria",
            Tags: [],
            ProvenanceKind: "stated",
            ProvenanceDate: "not-a-date",
            ProvenanceQualifier: null,
            Actor: "unit-test");

        var result = await handler.HandleAsync(invalidDateCommand);
        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
        Assert.Empty(repo.Audits);
    }

    [Fact]
    public async Task CreateNoteCommandHandler_EnforcesTitleAreaTagAndBodyCeilings_WithTypedValidationError()
    {
        var repo = new InMemoryFakeNoteRepository();
        var clock = new FixedUtcClock(FixedNow);
        var handler = new CreateNoteCommandHandler(repo, clock);

        CreateNoteCommand ValidCommand() => new(
            Title: new string('T', Note.MaxTitleChars),
            Body: "Valid body",
            Area: new string('a', Note.MaxAreaChars),
            Tags: Enumerable.Range(1, Note.MaxTagCount).Select(i => $"tag-{i:D2}").ToArray(),
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: null,
            Actor: "unit-test");

        // 1. Title > 300 chars -> ValidationError
        var titleTooLong = await handler.HandleAsync(ValidCommand() with { Title = new string('T', Note.MaxTitleChars + 1) });
        Assert.False(titleTooLong.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, titleTooLong.Error!.Value.Code);

        // 2. Area > 64 chars -> ValidationError
        var areaTooLong = await handler.HandleAsync(ValidCommand() with { Area = new string('a', Note.MaxAreaChars + 1) });
        Assert.False(areaTooLong.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, areaTooLong.Error!.Value.Code);

        // 3. Tags > 32 count -> ValidationError
        var tooManyTags = Enumerable.Range(1, Note.MaxTagCount + 1).Select(i => $"tag-{i}").ToArray();
        var tagsCountExceeded = await handler.HandleAsync(ValidCommand() with { Tags = tooManyTags });
        Assert.False(tagsCountExceeded.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, tagsCountExceeded.Error!.Value.Code);

        // 4. Single tag > 64 chars -> ValidationError
        var tagTooLong = await handler.HandleAsync(ValidCommand() with { Tags = [new string('x', Note.MaxTagChars + 1)] });
        Assert.False(tagTooLong.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, tagTooLong.Error!.Value.Code);

        // 5. Body > 8 MiB UTF-8 -> ValidationError
        var over8MiBBody = new string('B', Note.MaxBodyBytes + 1);
        var bodyTooLarge = await handler.HandleAsync(ValidCommand() with { Body = over8MiBBody });
        Assert.False(bodyTooLarge.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, bodyTooLarge.Error!.Value.Code);
        Assert.Empty(repo.Notes);
        Assert.Empty(repo.Audits);

        // 6. Exact boundary values (300 title, 64 area, 32 tags of 64 chars, 8 MiB body) -> Success
        var boundaryTags = Enumerable.Range(1, Note.MaxTagCount)
            .Select(i => $"t{i:D2}-" + new string('z', Note.MaxTagChars - 4))
            .ToArray();
        var exact8MiBBody = new string('A', Note.MaxBodyBytes);
        var boundarySuccess = await handler.HandleAsync(ValidCommand() with
        {
            Tags = boundaryTags,
            Body = exact8MiBBody
        });
        Assert.True(boundarySuccess.IsSuccess, boundarySuccess.Error?.Message);
        Assert.Single(repo.Notes);
        Assert.Single(repo.Audits);
    }

    [Fact]
    public async Task AppendAndReplaceSectionHandlers_EnforceInputAndResulting8MiBBodyCeilings_WithTypedValidationError()
    {
        var repo = new InMemoryFakeNoteRepository();
        var clock = new FixedUtcClock(FixedNow);
        var createHandler = new CreateNoteCommandHandler(repo, clock);
        var appendHandler = new AppendToNoteCommandHandler(repo, clock);
        var replaceHandler = new ReplaceNoteSectionCommandHandler(repo, clock);

        // Create a note close to 8 MiB (8 MiB - 100 bytes)
        const string prefix = "# Root\n## SectionA\nShort A.\n## SectionB\n";
        var fillerLen = Note.MaxBodyBytes - System.Text.Encoding.UTF8.GetByteCount(prefix) - 100;
        var initialBody = prefix + new string('X', fillerLen);

        var created = await createHandler.HandleAsync(new CreateNoteCommand(
            Title: "Near Limit Note",
            Body: initialBody,
            Area: "criteria",
            Tags: ["limit"],
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: null));
        Assert.True(created.IsSuccess);
        var noteId = created.Value!.Id;
        var initialToken = created.Value.VersionToken;

        // 1. Append with textToAppend > 8 MiB -> ValidationError
        var appendInputTooLarge = await appendHandler.HandleAsync(new AppendToNoteCommand(
            Id: noteId,
            TextToAppend: new string('Y', Note.MaxBodyBytes + 1)));
        Assert.False(appendInputTooLarge.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, appendInputTooLarge.Error!.Value.Code);

        // 2. Append where textToAppend is small (200 bytes) but resulting body > 8 MiB -> ValidationError
        var appendResultTooLarge = await appendHandler.HandleAsync(new AppendToNoteCommand(
            Id: noteId,
            TextToAppend: new string('Y', 200)));
        Assert.False(appendResultTooLarge.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, appendResultTooLarge.Error!.Value.Code);
        Assert.Single(repo.Audits); // Only the initial create audit entry exists

        // 3. ReplaceSection with newSectionBody > 8 MiB -> ValidationError
        var replaceInputTooLarge = await replaceHandler.HandleAsync(new ReplaceNoteSectionCommand(
            Id: noteId,
            Heading: "SectionA",
            NewSectionBody: "## SectionA\n" + new string('Z', Note.MaxBodyBytes),
            VersionToken: initialToken));
        Assert.False(replaceInputTooLarge.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, replaceInputTooLarge.Error!.Value.Code);

        // 4. ReplaceSection where newSectionBody is small (200 bytes) but resulting body > 8 MiB -> ValidationError
        var replaceResultTooLarge = await replaceHandler.HandleAsync(new ReplaceNoteSectionCommand(
            Id: noteId,
            Heading: "SectionA",
            NewSectionBody: "## SectionA\n" + new string('Z', 200),
            VersionToken: initialToken));
        Assert.False(replaceResultTooLarge.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, replaceResultTooLarge.Error!.Value.Code);
        Assert.Empty(repo.Revisions);
        Assert.Single(repo.Audits);
    }

    [Fact]
    public async Task GetNoteQueryHandler_ReturnsNoteDetailDto_SupportsPagination_AndValidatesBounds()
    {
        var repo = new InMemoryFakeNoteRepository();
        var clock = new FixedUtcClock(FixedNow);
        var createHandler = new CreateNoteCommandHandler(repo, clock);
        const string smallBody = "# Heading\nBody text.\n";
        var created = await createHandler.HandleAsync(new CreateNoteCommand(
            Title: "Sample Note",
            Body: smallBody,
            Area: "runbook",
            Tags: ["interview"],
            ProvenanceKind: "researched",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: null));

        var getHandler = new GetNoteQueryHandler(repo);
        var found = await getHandler.HandleAsync(new GetNoteQuery(created.Value!.Id));
        Assert.True(found.IsSuccess);
        Assert.Equal(created.Value.Id, found.Value!.Id);
        Assert.Equal(smallBody, found.Value.Body);
        Assert.Equal(smallBody.Length, found.Value.TotalChars);
        Assert.False(found.Value.Truncated);
        Assert.Null(found.Value.NextOffset);
        Assert.Equal(0, found.Value.RevisionCount);

        // Create a >200,000-character note (250,000 chars) and verify default truncation + paging
        var largeBody = string.Concat(
            new string('A', 100_000),
            new string('B', 100_000),
            new string('C', 50_000));
        var largeCreated = await createHandler.HandleAsync(new CreateNoteCommand(
            Title: "Paged Note",
            Body: largeBody,
            Area: "criteria",
            Tags: ["paged"],
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: null));

        var page1 = await getHandler.HandleAsync(new GetNoteQuery(largeCreated.Value!.Id));
        Assert.True(page1.IsSuccess);
        Assert.Equal(200_000, page1.Value!.Body.Length);
        Assert.Equal(250_000, page1.Value.TotalChars);
        Assert.True(page1.Value.Truncated);
        Assert.Equal(200_000, page1.Value.NextOffset);
        Assert.Equal(Sha256Hasher.ComputeLowerHex(largeBody), page1.Value.BodyHash);

        var page2 = await getHandler.HandleAsync(new GetNoteQuery(
            Id: largeCreated.Value.Id,
            Offset: page1.Value.NextOffset!.Value,
            MaxChars: 200_000));
        Assert.True(page2.IsSuccess);
        Assert.Equal(50_000, page2.Value!.Body.Length);
        Assert.Equal(250_000, page2.Value.TotalChars);
        Assert.False(page2.Value.Truncated);
        Assert.Null(page2.Value.NextOffset);
        Assert.Equal(largeBody, page1.Value.Body + page2.Value.Body);

        // Validation errors on invalid offset / maxChars
        var negativeOffset = await getHandler.HandleAsync(new GetNoteQuery(created.Value.Id, Offset: -1));
        Assert.False(negativeOffset.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, negativeOffset.Error!.Value.Code);

        var zeroMaxChars = await getHandler.HandleAsync(new GetNoteQuery(created.Value.Id, Offset: 0, MaxChars: 0));
        Assert.False(zeroMaxChars.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, zeroMaxChars.Error!.Value.Code);

        var excessiveMaxChars = await getHandler.HandleAsync(new GetNoteQuery(created.Value.Id, Offset: 0, MaxChars: Note.MaxGetMaxChars + 1));
        Assert.False(excessiveMaxChars.IsSuccess);
        Assert.Equal(ErrorCode.ValidationError, excessiveMaxChars.Error!.Value.Code);

        var missing = await getHandler.HandleAsync(new GetNoteQuery(Guid.CreateVersion7()));
        Assert.False(missing.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, missing.Error!.Value.Code);
    }

    [Fact]
    public async Task AppendToNoteCommandHandler_AppendsWithoutVersionTokenOrRevision_AndReturnsNoteUpdatedDto()
    {
        var repo = new InMemoryFakeNoteRepository();
        var clock = new FixedUtcClock(FixedNow);
        var createHandler = new CreateNoteCommandHandler(repo, clock);
        var created = await createHandler.HandleAsync(new CreateNoteCommand(
            Title: "Append Target",
            Body: "# Notes\nLine 1.\n",
            Area: "general",
            Tags: null,
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: null));

        var appendHandler = new AppendToNoteCommandHandler(repo, new FixedUtcClock(FixedNow.AddMinutes(5)));
        var appended = await appendHandler.HandleAsync(new AppendToNoteCommand(
            Id: created.Value!.Id,
            TextToAppend: "Line 2.\n",
            Actor: "mcp-stdio"));

        Assert.True(appended.IsSuccess);
        Assert.NotEqual(created.Value.VersionToken, appended.Value!.VersionToken);
        Assert.Empty(repo.Revisions);
        Assert.Equal(2, repo.Audits.Count);
        Assert.Equal(AuditOperation.NoteAppend, repo.Audits[1].Operation);
        Assert.Equal("Line 2.\n", repo.Audits[1].AppendedText);

        var missingAppend = await appendHandler.HandleAsync(new AppendToNoteCommand(
            Id: Guid.CreateVersion7(),
            TextToAppend: "Line 2.\n"));
        Assert.False(missingAppend.IsSuccess);
        Assert.Equal(ErrorCode.NotFound, missingAppend.Error!.Value.Code);
    }

    [Fact]
    public async Task ReplaceNoteSectionCommandHandler_ReplacesSection_SavesRevision_AndEnforcesVersionTokenAndHeadingChecks()
    {
        var repo = new InMemoryFakeNoteRepository();
        var clock = new FixedUtcClock(FixedNow);
        var createHandler = new CreateNoteCommandHandler(repo, clock);
        var created = await createHandler.HandleAsync(new CreateNoteCommand(
            Title: "Section Target",
            Body: "# Overview\n## Alpha\nOld alpha.\n## Beta\nKeep beta.\n",
            Area: "criteria",
            Tags: ["test"],
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: null));

        var replaceHandler = new ReplaceNoteSectionCommandHandler(repo, new FixedUtcClock(FixedNow.AddMinutes(10)));

        // 1. Missing heading -> SectionNotFound
        var missingSection = await replaceHandler.HandleAsync(new ReplaceNoteSectionCommand(
            Id: created.Value!.Id,
            Heading: "Gamma",
            NewSectionBody: "## Gamma\nNew.\n",
            VersionToken: created.Value.VersionToken));
        Assert.False(missingSection.IsSuccess);
        Assert.Equal(ErrorCode.SectionNotFound, missingSection.Error!.Value.Code);

        // 2. Valid replacement -> creates revision #1
        var replaced = await replaceHandler.HandleAsync(new ReplaceNoteSectionCommand(
            Id: created.Value.Id,
            Heading: "Alpha",
            NewSectionBody: "## Alpha\nUpdated alpha.\n",
            VersionToken: created.Value.VersionToken));
        Assert.True(replaced.IsSuccess);
        Assert.Equal(1, replaced.Value!.RevisionCount);
        Assert.Single(repo.Revisions);
        Assert.Equal("# Overview\n## Alpha\nOld alpha.\n## Beta\nKeep beta.\n", repo.Revisions[0].PriorBody);

        // 3. Stale version token -> StaleVersionToken
        var stale = await replaceHandler.HandleAsync(new ReplaceNoteSectionCommand(
            Id: created.Value.Id,
            Heading: "Alpha",
            NewSectionBody: "## Alpha\nSecond update with stale token.\n",
            VersionToken: created.Value.VersionToken));
        Assert.False(stale.IsSuccess);
        Assert.Equal(ErrorCode.StaleVersionToken, stale.Error!.Value.Code);
    }

    [Fact]
    public async Task SearchNotesQueryHandler_DelegatesCriteriaAndMapsHits()
    {
        var repo = new InMemoryFakeNoteRepository();
        var clock = new FixedUtcClock(FixedNow);
        var createHandler = new CreateNoteCommandHandler(repo, clock);
        await createHandler.HandleAsync(new CreateNoteCommand(
            Title: "Distributed Kafka Architecture",
            Body: "# Kafka\nEvent streaming criteria.\n",
            Area: "criteria",
            Tags: ["kafka", "backend"],
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-17",
            ProvenanceQualifier: null));

        var searchHandler = new SearchNotesQueryHandler(repo);
        var searchResult = await searchHandler.HandleAsync(new SearchNotesQuery(
            Query: "Kafka",
            Tags: ["kafka"],
            Area: "criteria",
            IncludeArchived: false,
            Limit: 10));

        Assert.True(searchResult.IsSuccess);
        Assert.Single(searchResult.Value!.Results);
        Assert.Equal("Distributed Kafka Architecture", searchResult.Value.Results[0].Title);
    }

    private sealed class FixedUtcClock(DateTime utcNow) : IUtcClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class InMemoryFakeNoteRepository : INoteRepository, INoteTransactionContext
    {
        public Dictionary<Guid, Note> Notes { get; } = [];
        public List<NoteRevision> Revisions { get; } = [];
        public List<AuditLogEntry> Audits { get; } = [];

        public Task<Result<StoreReadinessStatus>> CheckReadinessAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<StoreReadinessStatus>.Success(new StoreReadinessStatus(true, "in-memory", "rs0", 0.1)));

        public Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(
            Func<INoteTransactionContext, CancellationToken, Task<Result<TResponse>>> work,
            CancellationToken cancellationToken = default) =>
            work(this, cancellationToken);

        public Task<Result<Note>> GetByIdAsync(string ownerId, Guid noteId, CancellationToken cancellationToken = default)
        {
            if (Notes.TryGetValue(noteId, out var note) && note.OwnerId == ownerId)
            {
                return Task.FromResult(Result<Note>.Success(CloneNote(note)));
            }

            return Task.FromResult(Result<Note>.Failure(ErrorCode.NotFound, $"Note '{noteId}' not found."));
        }

        public Task<Result<IReadOnlyList<NoteSearchHit>>> SearchAsync(NoteSearchCriteria criteria, CancellationToken cancellationToken = default)
        {
            var matches = Notes.Values
                .Where(n => n.OwnerId == criteria.OwnerId)
                .Where(n => criteria.IncludeArchived || n.Status == NoteStatus.Active)
                .Where(n => string.IsNullOrWhiteSpace(criteria.Area) || string.Equals(n.Area, Note.NormalizeArea(criteria.Area), StringComparison.Ordinal))
                .Where(n => criteria.Tags is null || criteria.Tags.All(t => n.Tags.Contains(t.Trim().ToLowerInvariant())))
                .Where(n => string.IsNullOrWhiteSpace(criteria.Query) ||
                            n.Title.Contains(criteria.Query, StringComparison.OrdinalIgnoreCase) ||
                            n.Body.Contains(criteria.Query, StringComparison.OrdinalIgnoreCase))
                .Select(n => new NoteSearchHit(CloneNote(n), 1.5, n.Body[..Math.Min(120, n.Body.Length)]))
                .ToList();

            return Task.FromResult(Result<IReadOnlyList<NoteSearchHit>>.Success(matches));
        }

        public Task<Result<IReadOnlyList<Note>>> GetImportedBySourceFileAsync(string ownerId, string sourceFileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<Note>>.Success([]));

        public Task<Result<IReadOnlyList<NoteRevision>>> GetRevisionsAsync(string ownerId, Guid noteId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<NoteRevision>>.Success(Revisions.Where(r => r.NoteId == noteId).ToList()));

        public Task<Result<IReadOnlyList<AuditLogEntry>>> GetAuditEntriesAsync(string ownerId, Guid? noteId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<IReadOnlyList<AuditLogEntry>>.Success(Audits.Where(a => noteId is null || a.TargetNoteId == noteId).ToList()));

        public Task<Result<NoteWriteOutcome>> CreateAsync(Note note, AuditLogEntry auditEntry, CancellationToken cancellationToken = default)
        {
            Notes[note.Id] = CloneNote(note);
            Audits.Add(auditEntry);
            return Task.FromResult(Result<NoteWriteOutcome>.Success(new NoteWriteOutcome(CloneNote(note), auditEntry)));
        }

        public Task<Result<NoteWriteOutcome>> AppendAsync(string ownerId, Guid noteId, string textToAppend, string actor, DateTime occurredAtUtc, CancellationToken cancellationToken = default)
        {
            if (!Notes.TryGetValue(noteId, out var existing) || existing.OwnerId != ownerId)
            {
                return Task.FromResult(Result<NoteWriteOutcome>.Failure(ErrorCode.NotFound, $"Note '{noteId}' not found."));
            }

            var appendBytes = System.Text.Encoding.UTF8.GetByteCount(textToAppend);
            var resultingBytes = System.Text.Encoding.UTF8.GetByteCount(existing.Body) + appendBytes;
            if (resultingBytes > Note.MaxBodyBytes)
            {
                return Task.FromResult(Result<NoteWriteOutcome>.Failure(
                    ErrorCode.ValidationError,
                    $"Resulting note body UTF-8 size ({resultingBytes} bytes) after append exceeds maximum allowed ({Note.MaxBodyBytes} bytes / 8 MiB)."));
            }

            var working = CloneNote(existing);
            var before = AuditContentSummary.FromNote(working);
            working.AppendBody(textToAppend, occurredAtUtc);
            var after = AuditContentSummary.FromNote(working);
            var audit = AuditLogEntry.CreateForAppend(ownerId, actor, working.Id, textToAppend, working.VersionToken, occurredAtUtc, before, after);
            Notes[noteId] = working;
            Audits.Add(audit);
            return Task.FromResult(Result<NoteWriteOutcome>.Success(new NoteWriteOutcome(CloneNote(working), audit)));
        }

        public Task<Result<NoteWriteOutcome>> ReplaceSectionAsync(Note updatedNote, Guid expectedVersionToken, NoteRevision priorBodyRevision, AuditLogEntry auditEntry, CancellationToken cancellationToken = default)
        {
            if (!Notes.TryGetValue(updatedNote.Id, out var existing))
            {
                return Task.FromResult(Result<NoteWriteOutcome>.Failure(ErrorCode.NotFound, $"Note '{updatedNote.Id}' not found."));
            }

            if (existing.VersionToken != expectedVersionToken)
            {
                return Task.FromResult(Result<NoteWriteOutcome>.Failure(
                    ErrorCode.StaleVersionToken,
                    "Stale version token.",
                    new Dictionary<string, object?> { ["currentVersionToken"] = existing.VersionToken.ToString() }));
            }

            Notes[updatedNote.Id] = CloneNote(updatedNote);
            Revisions.Add(priorBodyRevision);
            Audits.Add(auditEntry);
            return Task.FromResult(Result<NoteWriteOutcome>.Success(new NoteWriteOutcome(CloneNote(updatedNote), auditEntry, priorBodyRevision)));
        }

        public Task<Result<NoteWriteOutcome>> ArchiveAsync(Note archivedNote, Guid expectedVersionToken, AuditLogEntry auditEntry, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<NoteWriteOutcome>> UpsertImportedSectionAsync(UpsertImportedSectionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static Note CloneNote(Note n) => Note.Rehydrate(
            n.Id,
            n.OwnerId,
            n.Title,
            n.Body,
            n.CurrentBodyHash,
            n.Tags.ToArray(),
            n.Area,
            n.Provenance,
            n.ImportMetadata,
            n.Status,
            n.VersionToken,
            n.RevisionCount,
            n.CreatedAtUtc,
            n.UpdatedAtUtc);
    }
}
