using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Pipeline;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure.Csv;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class ApplicationsImportStepTests
{
    private const string TestOwnerId = "test-owner";
    private static readonly DateOnly TestDefaultDate = new(2026, 10, 24);

    [Fact]
    public async Task ApplicationsStep_CreatesApplication_TransitionsPosting_AndAppendsRejectionEvent()
    {
        var tempDir = CreateTempDir();
        try
        {
            var csv = """
                kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
                application,Acme Corp,Software Architect,REQ-101,2026-03-10,true,2026-04-01,215000,true,strong_fit,tailored,Jane Doe (Hiring Mgr),contains_people;date_is_stated_not_submitted,Applied via referral,,
                standalone_note,Globex,General Inquiry,,2026-03-08,,,,,,,,,Informational interview notes,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), csv);

            var inMemoryCtx = new InMemoryApplicationContext();

            // Pre-seed an existing scored posting for REQ-101
            var existingPosting = Posting.Create(
                ownerId: TestOwnerId,
                companyId: Guid.NewGuid(),
                title: "Software Architect",
                sourceKind: SourceKind.Aggregator,
                requisitionId: "REQ-101",
                disposition: PostingDisposition.Scored);
            inMemoryCtx.SavedPostings.Add(existingPosting);

            var parser = new Rfc4180CsvParser();
            var step = new ApplicationsStep(parser, inMemoryCtx);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };
            context.PostingsByReqOrUrl["REQ-101"] = existingPosting;
            context.Summary.UnclaimedAppliedPostingTitles.Add(existingPosting.Title);
            context.Summary.UnclaimedAppliedPostings = 1;

            var result = await step.ExecuteAsync(context);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, context.Summary.ApplicationsCreated);
            Assert.Equal(1, context.Summary.StandaloneNotesCreated);
            Assert.Equal(1, context.Summary.PostingsUpdated);
            Assert.Equal(0, context.Summary.UnclaimedAppliedPostings); // Successfully claimed!
            Assert.Empty(context.Summary.UnclaimedAppliedPostingTitles);

            // Verify posting was transitioned to Rejected (due to rejected=true)
            Assert.Equal(PostingDisposition.Rejected, existingPosting.Disposition);

            // Verify application created
            var app = inMemoryCtx.SavedApplications.FirstOrDefault(a => a.PostingId == existingPosting.Id);
            Assert.NotNull(app);
            Assert.Equal(ApplicationOutcome.Rejected, app.Outcome);
            Assert.Equal(215000, app.DesiredSalary.Min);
            Assert.Equal(ResumeVariant.Tailored, app.ResumeVariant);

            // Verify application events: 1 Applied, 1 Rejected
            var events = inMemoryCtx.SavedEvents.Where(e => e.ApplicationId == app.Id).ToList();
            Assert.Equal(2, events.Count);
            Assert.Contains(events, e => e.EventType == ApplicationEventType.Applied && e.Notes!.Contains("date_is_stated_not_submitted"));
            Assert.Contains(events, e => e.EventType == ApplicationEventType.Rejected);

            // Verify standalone note created in job-search area
            var note = inMemoryCtx.SavedNotes.FirstOrDefault(n => n.Area == "job-search");
            Assert.NotNull(note);
            Assert.Contains("Informational interview notes", note.Body);
            Assert.Equal("Globex - General Inquiry", note.Title);

            // Verify zero contacts created and people detected reported
            Assert.Single(context.Summary.PeopleDetected);
            Assert.Contains("Jane Doe", context.Summary.PeopleDetected[0]);

            // Verify audit operations
            var appAudits = inMemoryCtx.AppendedAuditEntries.Where(a => a.EntityType == "application").ToList();
            Assert.Single(appAudits);
            Assert.Equal(AuditOperation.ImportApplicationCreate, appAudits[0].Operation);

            var eventAudits = inMemoryCtx.AppendedAuditEntries.Where(a => a.EntityType == "application_event").ToList();
            Assert.Equal(2, eventAudits.Count);
            Assert.All(eventAudits, a => Assert.Equal(AuditOperation.ImportApplicationEventAppend, a.Operation));

            var noteAudits = inMemoryCtx.AppendedAuditEntries.Where(a => a.EntityType == "note").ToList();
            Assert.Single(noteAudits);
            Assert.Equal(AuditOperation.ImportCreate, noteAudits[0].Operation); // Standalone note reuses import_create
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rr_app_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class InMemoryApplicationContext : ITrackingTransactionContext
    {
        public List<Company> SavedCompanies { get; } = new();
        public List<Posting> SavedPostings { get; } = new();
        public List<RoleRadar.Domain.Tracking.Application> SavedApplications { get; } = new();
        public List<ApplicationEvent> SavedEvents { get; } = new();
        public List<Note> SavedNotes { get; } = new();
        public List<AuditLogEntry> AppendedAuditEntries { get; } = new();

        public ICompanyRepository Companies => new FakeCompanyRepository(SavedCompanies);
        public IPostingRepository Postings => new FakePostingRepository(SavedPostings);
        public ICriterionRepository Criteria => throw new NotImplementedException();
        public IApplicationRepository Applications => new FakeApplicationRepository(SavedApplications, SavedEvents);
        public IContactRepository Contacts => throw new NotImplementedException();
        public ISearchRunRepository SearchRuns => throw new NotImplementedException();

        public Task AppendAuditEntryAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            AppendedAuditEntries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<Result<bool>> UpsertNoteAsync(Note note, CancellationToken cancellationToken = default)
        {
            SavedNotes.Add(note);
            return Task.FromResult(Result<bool>.Success(true));
        }

        public Task<Result<bool>> NoteExistsAsync(string ownerId, string title, string area, CancellationToken cancellationToken = default)
        {
            var exists = SavedNotes.Any(n => n.OwnerId == ownerId && n.Title == title && n.Area == area);
            return Task.FromResult(Result<bool>.Success(exists));
        }

        private sealed class FakeCompanyRepository : ICompanyRepository
        {
            private readonly List<Company> _companies;
            public FakeCompanyRepository(List<Company> companies) => _companies = companies;
            public Task<Result<Company?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Company?>.Success(_companies.FirstOrDefault(c => c.Id == id)));
            public Task<Result<Company?>> GetByNormalizedNameAsync(string ownerId, string normalizedName, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Company?>.Success(_companies.FirstOrDefault(c => c.NormalizedName == normalizedName)));
            public Task<Result<IReadOnlyList<Company>>> ListAsync(string ownerId, CompanyStatus? status = null, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<Company>>.Success(_companies));
            public Task<Result<IReadOnlyList<CareerSource>>> GetCareerSourcesAsync(string ownerId, Guid companyId, bool includeInactive = false, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<CareerSource>>.Success(Array.Empty<CareerSource>()));
            public Task<Result<CareerSource?>> GetCareerSourceByCompositeKeyAsync(string ownerId, Guid companyId, string atsType, string? tenant, string? slug, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<CareerSource?>.Success(null));
            public Task<Result<bool>> UpsertCompanyAsync(Company company, CancellationToken cancellationToken = default)
            {
                var index = _companies.FindIndex(c => c.Id == company.Id);
                if (index >= 0) _companies[index] = company;
                else _companies.Add(company);
                return Task.FromResult(Result<bool>.Success(true));
            }
            public Task<Result<bool>> UpsertCareerSourceAsync(CareerSource careerSource, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<bool>.Success(true));
            public Task<Result<TResponse>> ExecuteInTransactionAsync<TResponse>(Func<ITrackingTransactionContext, CancellationToken, Task<Result<TResponse>>> work, CancellationToken cancellationToken = default) =>
                throw new NotImplementedException();
        }

        private sealed class FakePostingRepository : IPostingRepository
        {
            private readonly List<Posting> _postings;
            public FakePostingRepository(List<Posting> postings) => _postings = postings;
            public Task<Result<Posting?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Posting?>.Success(_postings.FirstOrDefault(p => p.Id == id)));
            public Task<Result<Posting?>> GetByDedupKeyAAsync(string ownerId, string dedupKeyA, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Posting?>.Success(_postings.FirstOrDefault(p => p.DedupKeyA == dedupKeyA)));
            public Task<Result<Posting?>> GetByDedupKeyBAsync(string ownerId, string dedupKeyB, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Posting?>.Success(_postings.FirstOrDefault(p => p.DedupKeyB == dedupKeyB)));
            public Task<Result<Posting?>> GetByDedupKeyCAsync(string ownerId, string dedupKeyC, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Posting?>.Success(_postings.FirstOrDefault(p => p.DedupKeyC == dedupKeyC)));
            public Task<Result<IReadOnlyList<PostingCandidate>>> FindCandidatesByCompanyAndTitleAsync(string ownerId, Guid companyId, string normalizedTitle, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<PostingCandidate>>.Success(_postings
                    .Where(p => p.CompanyId == companyId && p.NormalizedTitle == normalizedTitle)
                    .Select(p => new PostingCandidate(p.Id, p.Title, p.Disposition, p.DispositionReason))
                    .ToList()));
            public Task<Result<IReadOnlyList<Posting>>> ListPostingsAsync(string ownerId, Guid? companyId = null, PostingDisposition? disposition = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<Posting>>.Success(_postings));
            public Task<Result<IReadOnlyList<Posting>>> ListDecidedPostingsAsync(string ownerId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<Posting>>.Success(_postings));
            public Task<Result<int>> CountPostingsAsync(string ownerId, Guid? companyId = null, PostingDisposition? disposition = null, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<int>.Success(_postings.Count));
            public Task<Result<int>> CountDecidedPostingsAsync(string ownerId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<int>.Success(_postings.Count));
            public Task<Result<bool>> UpsertPostingAsync(Posting posting, CancellationToken cancellationToken = default)
            {
                var index = _postings.FindIndex(p => p.Id == posting.Id);
                if (index >= 0) _postings[index] = posting;
                else _postings.Add(posting);
                return Task.FromResult(Result<bool>.Success(true));
            }
            public Task<Result<bool>> AppendSignalAsync(PostingSignal signal, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<bool>.Success(true));
            public Task<Result<IReadOnlyList<PostingSignal>>> GetSignalsAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<PostingSignal>>.Success(Array.Empty<PostingSignal>()));
            public Task<Result<ILookup<Guid, PostingSignal>>> GetSignalsForPostingsAsync(string ownerId, IEnumerable<Guid> postingIds, CancellationToken cancellationToken = default) =>
                throw new NotImplementedException();
        }

        private sealed class FakeApplicationRepository : IApplicationRepository
        {
            private readonly List<RoleRadar.Domain.Tracking.Application> _applications;
            private readonly List<ApplicationEvent> _events;

            public FakeApplicationRepository(List<RoleRadar.Domain.Tracking.Application> applications, List<ApplicationEvent> events)
            {
                _applications = applications;
                _events = events;
            }

            public Task<Result<RoleRadar.Domain.Tracking.Application?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<RoleRadar.Domain.Tracking.Application?>.Success(_applications.FirstOrDefault(a => a.Id == id)));

            public Task<Result<RoleRadar.Domain.Tracking.Application?>> GetByPostingIdAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<RoleRadar.Domain.Tracking.Application?>.Success(_applications.FirstOrDefault(a => a.PostingId == postingId)));

            public Task<Result<bool>> UpsertApplicationAsync(RoleRadar.Domain.Tracking.Application application, CancellationToken cancellationToken = default)
            {
                var index = _applications.FindIndex(a => a.Id == application.Id);
                if (index >= 0) _applications[index] = application;
                else _applications.Add(application);
                return Task.FromResult(Result<bool>.Success(true));
            }

            public Task<Result<bool>> AppendEventAsync(ApplicationEvent appEvent, CancellationToken cancellationToken = default)
            {
                _events.Add(appEvent);
                return Task.FromResult(Result<bool>.Success(true));
            }

            public Task<Result<IReadOnlyList<ApplicationEvent>>> GetEventsAsync(string ownerId, Guid applicationId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<ApplicationEvent>>.Success(_events.Where(e => e.ApplicationId == applicationId).ToList()));

            public Task<Result<IReadOnlyList<RoleRadar.Domain.Tracking.Application>>> ListApplicationsAsync(string ownerId, string? outcome = null, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<RoleRadar.Domain.Tracking.Application>>.Success(_applications));

            public Task<Result<int>> CountApplicationsAsync(string ownerId, string? outcome = null, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<int>.Success(_applications.Count));
        }
    }
}
