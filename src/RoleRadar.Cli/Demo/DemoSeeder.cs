using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using ApplicationEntity = RoleRadar.Domain.Tracking.Application;

namespace RoleRadar.Cli.Demo;

/// <summary>What one seed run created. All counts are rows created by this run; <see cref="AlreadySeeded"/> means none.</summary>
public sealed record DemoSeedReport(
    bool AlreadySeeded,
    int Companies,
    int Postings,
    int Applications,
    int ApplicationEvents,
    int Notes,
    int SearchRuns,
    int PostingSignals,
    int CareerSources);

/// <summary>
/// Writes the fictional demo dataset through the real repositories and the note command handler, so every derived field
/// comes from the repository choke point and notes are audited like any other. Idempotent by natural key: companies by
/// normalised name, career sources by their composite key, postings by normalised URL, one application per posting,
/// notes by title and area, search runs by start time. Each row is looked up first and created with a version 7 id only
/// when missing; an existing application's timeline and an existing posting's signals are never appended to. Notes are
/// written first so the hero posting can name its assessment note. The caller runs the demo guard before this (ADR 0039).
/// </summary>
public sealed class DemoSeeder(IServiceProvider services)
{
    public async Task<DemoSeedReport> SeedAsync(DateOnly asOf, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var companies = sp.GetRequiredService<ICompanyRepository>();
        var postings = sp.GetRequiredService<IPostingRepository>();
        var applications = sp.GetRequiredService<IApplicationRepository>();
        var searchRuns = sp.GetRequiredService<ISearchRunRepository>();
        var noteRepository = sp.GetRequiredService<INoteRepository>();
        var createNote = sp.GetRequiredService<ICommandHandler<CreateNoteCommand, NoteCreatedDto>>();
        var owner = OwnerId.Default.Value;

        int companiesCreated = 0, postingsCreated = 0, applicationsCreated = 0, eventsCreated = 0, notesCreated = 0, runsCreated = 0;
        int signalsCreated = 0, sourcesCreated = 0;

        // Notes, through the audited command handler.
        var noteIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var note in DemoDataset.Notes)
        {
            // The natural key is title and area. The registered tracking context cannot see notes, so look through the
            // note repository (archived notes included).
            var inArea = Unwrap(await noteRepository.SearchAsync(
                new NoteSearchCriteria(owner, Area: note.Area, IncludeArchived: true, Limit: 100), cancellationToken));
            var existingNote = inArea.FirstOrDefault(hit => string.Equals(hit.Note.Title, note.Title.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existingNote is not null)
            {
                noteIds[note.Title] = existingNote.Note.Id;
                continue;
            }

            var created = await createNote.HandleAsync(
                new CreateNoteCommand(
                    note.Title,
                    note.Body,
                    note.Area,
                    note.Tags,
                    note.Kind,
                    DateText(asOf, note.ProvenanceDaysBeforeAsOf),
                    ProvenanceQualifier: null,
                    Actor: "demo-seed",
                    OwnerId: owner),
                cancellationToken);
            if (!created.IsSuccess)
            {
                throw new InvalidOperationException("Demo note could not be created: " + created.Error?.Message);
            }

            noteIds[note.Title] = created.Value!.Id;
            notesCreated++;
        }

        // Companies.
        var companyIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var demo in DemoDataset.Companies)
        {
            var candidate = Company.Create(
                owner,
                demo.Name,
                description: demo.Description,
                ownershipType: demo.Ownership,
                headquarters: demo.Headquarters,
                categoryTags: demo.CategoryTags,
                status: demo.Status,
                now: Instant(asOf, 400));
            var existing = Unwrap(await companies.GetByNormalizedNameAsync(owner, candidate.NormalizedName, cancellationToken));
            if (existing is not null)
            {
                companyIds[demo.Name] = existing.Id;
            }
            else
            {
                Ensure(await companies.UpsertCompanyAsync(candidate, cancellationToken));
                companyIds[demo.Name] = candidate.Id;
                companiesCreated++;
            }

            // The career source is stored the way the company editor stores one: provider "custom", the URL as the slug.
            if (demo.CareerSourceUrl is { } sourceUrl &&
                Unwrap(await companies.GetCareerSourceByCompositeKeyAsync(owner, companyIds[demo.Name], CareerSourceProvider, null, sourceUrl, cancellationToken)) is null)
            {
                Ensure(await companies.UpsertCareerSourceAsync(
                    CareerSource.Create(owner, companyIds[demo.Name], CareerSourceProvider, slug: sourceUrl, fetchMethod: FetchMethod.Browser, now: Instant(asOf, 400)),
                    cancellationToken));
                sourcesCreated++;
            }
        }

        // Postings, applications and their timelines.
        foreach (var demo in DemoDataset.Postings)
        {
            var firstSeen = Instant(asOf, demo.FirstSeenDaysBeforeAsOf);
            var candidate = Posting.Create(
                ownerId: owner,
                companyId: companyIds[demo.Company],
                title: demo.Title,
                originalUrl: demo.Url,
                location: demo.Remote == RemoteType.Remote ? "Remote" : "United States",
                remoteType: demo.Remote,
                compensation: demo.Compensation,
                score: demo.Score,
                disposition: demo.Disposition,
                dispositionReason: demo.Reason,
                screeningNoteId: demo.AssessmentNoteTitle is { } assessment ? noteIds[assessment] : null,
                now: firstSeen);

            var posting = Unwrap(await postings.GetByDedupKeyBAsync(owner, candidate.DedupKeyB!, cancellationToken));
            if (posting is null)
            {
                Ensure(await postings.UpsertPostingAsync(candidate, cancellationToken));
                posting = candidate;
                postingsCreated++;
            }

            if (demo.Signals.Count > 0 && Unwrap(await postings.GetSignalsAsync(owner, posting.Id, cancellationToken)).Count == 0)
            {
                foreach (var signal in demo.Signals)
                {
                    Ensure(await postings.AppendSignalAsync(
                        new PostingSignal(
                            Guid.CreateVersion7(),
                            owner,
                            posting.Id,
                            signal.Type,
                            signal.Evidence,
                            Instant(asOf, signal.DaysBeforeAsOf),
                            signal.DeadlineDaysBeforeAsOf is { } deadline ? DateText(asOf, deadline) : null),
                        cancellationToken));
                    signalsCreated++;
                }
            }

            if (demo.AppliedDaysBeforeAsOf is not { } appliedDays)
            {
                continue;
            }

            var application = Unwrap(await applications.GetByPostingIdAsync(owner, posting.Id, cancellationToken));
            if (application is not null)
            {
                continue;
            }

            var appliedAt = Instant(asOf, appliedDays);
            application = ApplicationEntity.Create(
                ownerId: owner,
                postingId: posting.Id,
                submittedDate: DateText(asOf, appliedDays),
                resumeVariant: ResumeVariant.Tailored,
                now: appliedAt);
            Ensure(await applications.UpsertApplicationAsync(application, cancellationToken));
            Ensure(await applications.AppendEventAsync(
                ApplicationEvent.Create(owner, application.Id, DateText(asOf, appliedDays), ApplicationEventType.Applied, now: appliedAt), cancellationToken));
            applicationsCreated++;
            eventsCreated++;

            foreach (var ev in demo.Events)
            {
                var when = Instant(asOf, ev.DaysBeforeAsOf);
                Ensure(await applications.AppendEventAsync(
                    ApplicationEvent.Create(owner, application.Id, DateText(asOf, ev.DaysBeforeAsOf), ev.Type, now: when), cancellationToken));
                application.ApplyEvent(ev.Type, null, when);
                eventsCreated++;
            }

            // Persist the outcome the events produced; the replace recomputes every milestone from the events.
            Ensure(await applications.UpsertApplicationAsync(application, cancellationToken));
        }

        // Search runs. Their natural key is the start time, which moves with the as-of date, so they are only written
        // when this run created tracker rows; a second run on a seeded database changes nothing, whatever its as-of date.
        var trackerRowsCreated = companiesCreated + postingsCreated + applicationsCreated > 0;
        foreach (var run in trackerRowsCreated ? DemoDataset.SearchRuns : Array.Empty<DemoSearchRun>())
        {
            var startedAt = Instant(asOf, run.DaysBeforeAsOf);
            if (Unwrap(await searchRuns.GetByStartedAtAsync(owner, startedAt, cancellationToken)) is not null)
            {
                continue;
            }

            Ensure(await searchRuns.UpsertSearchRunAsync(
                SearchRun.Create(owner, startedAt, run.NewPostings, createdAt: startedAt), cancellationToken));
            runsCreated++;
        }

        var total = companiesCreated + postingsCreated + applicationsCreated + notesCreated + runsCreated + signalsCreated + sourcesCreated;
        return new DemoSeedReport(
            total == 0, companiesCreated, postingsCreated, applicationsCreated, eventsCreated, notesCreated, runsCreated, signalsCreated, sourcesCreated);
    }

    /// <summary>The provider name the company editor gives a career source with no provider (companies.$id.tsx).</summary>
    private const string CareerSourceProvider = "custom";

    private static DateTimeOffset Instant(DateOnly asOf, int daysBefore) =>
        new(asOf.AddDays(-daysBefore).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc), TimeSpan.Zero);

    private static string DateText(DateOnly asOf, int daysBefore) =>
        asOf.AddDays(-daysBefore).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static T Unwrap<T>(Result<T> result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("Demo seed read failed: " + result.Error?.Message);
        }

        return result.Value!;
    }

    private static void Ensure<T>(Result<T> result)
    {
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException("Demo seed write failed: " + result.Error?.Message);
        }
    }
}
