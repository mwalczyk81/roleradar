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

public sealed class PostingImportStepTests
{
    private const string TestOwnerId = "test-owner";
    private static readonly DateOnly TestDefaultDate = new(2026, 10, 24);

    [Fact]
    public async Task PostingsJsonStep_DropsTier_MapsCompHigh_MapsCultureLevel_AndFlagsOutOfRangeScore()
    {
        var tempDir = CreateTempDir();
        try
        {
            var json = """
                {
                    "acme-principal-architect": {
                        "added": "2026-03-01",
                        "comp": "250k base",
                        "compHigh": true,
                        "company": "Acme Corp",
                        "culture": "Healthy remote culture",
                        "cultureLevel": "stable",
                        "first": "2026-02-28",
                        "ghost": false,
                        "href": "https://example.com/job/301",
                        "loc": "Remote - US",
                        "note": "Screening note",
                        "order": 1,
                        "parent": "Acme Corp Parent",
                        "posted": "2026-02-25",
                        "score": 9.5,
                        "status": "applied",
                        "tier": "A",
                        "title": "Principal Architect"
                    },
                    "globex-staff-engineer": {
                        "added": "2026-03-05",
                        "comp": "220k base",
                        "compHigh": false,
                        "company": "Globex",
                        "culture": "Fast paced culture",
                        "cultureLevel": "non_standard_value",
                        "first": "2026-03-04",
                        "ghost": true,
                        "href": "https://example.com/job/302",
                        "loc": "New York, NY",
                        "note": "Staff eng note",
                        "order": 2,
                        "parent": "Globex International",
                        "posted": "2026-03-01",
                        "score": 12.0,
                        "status": "lead",
                        "tier": "B",
                        "title": "Staff Engineer"
                    },
                    "cyberdyne-cloud-arch": {
                        "added": "2026-03-10",
                        "company": "Cyberdyne Systems",
                        "culture": "Research environment",
                        "cultureLevel": "moderate",
                        "ghost": false,
                        "href": "https://example.com/job/303",
                        "loc": "Sunnyvale, CA",
                        "order": 3,
                        "score": 8.0,
                        "status": "scored",
                        "title": "Cloud Architect"
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), json);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var step = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);

            Assert.True(result.IsSuccess);
            Assert.Equal(3, context.Summary.PostingsCreated);

            // 1. First posting: compHigh true, culture stable, score 9.5, slug in audit (AtsTenant is null)
            var p1 = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.Title == "Principal Architect");
            Assert.NotNull(p1);
            Assert.True(p1.CompHighPriority);
            Assert.Null(p1.AtsTenant);
            Assert.Equal("Remote - US", p1.Location);
            Assert.Equal(CultureLevel.Stable, p1.CultureLevel);
            Assert.Equal(9.5m, p1.Score);
            Assert.Equal(PostingDisposition.Scored, p1.Disposition); // "applied" stages as "scored"

            var audit1 = inMemoryCtx.AppendedAuditEntries.FirstOrDefault(a => a.EntityId == p1.Id && a.Operation == AuditOperation.ImportPostingCreate);
            Assert.NotNull(audit1);
            Assert.NotNull(audit1!.Details);
            Assert.NotNull(audit1.Details!.After);
            Assert.Equal("acme-principal-architect", audit1.Details.After!["slug"]);

            // 2. Second posting: compHigh false, out-of-range score null, ghost true creates phantom signal
            var p2 = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.Title == "Staff Engineer");
            Assert.NotNull(p2);
            Assert.False(p2.CompHighPriority);
            Assert.Null(p2.AtsTenant);
            Assert.Equal(CultureLevel.Unknown, p2.CultureLevel);
            Assert.Null(p2.Score); // Out of range score stored as null
            Assert.True(context.Summary.DatesFlagged > 0); // Flagged count incremented
            Assert.Equal(1, context.Summary.CultureUnmapped);

            var ghostSig = inMemoryCtx.SavedSignals.FirstOrDefault(s => s.PostingId == p2.Id);
            Assert.NotNull(ghostSig);
            Assert.Equal(SignalType.Phantom, ghostSig.SignalType);

            var ghostAudit = inMemoryCtx.AppendedAuditEntries.FirstOrDefault(a => a.EntityId == p2.Id && a.Operation == AuditOperation.ImportPostingSignalAppend);
            Assert.NotNull(ghostAudit);

            // 3. Third posting: compHigh missing (null), status scored (not in unclaimed applied)
            var p3 = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.Title == "Cloud Architect");
            Assert.NotNull(p3);
            Assert.Null(p3.CompHighPriority);
            Assert.Null(p3.AtsTenant);
            Assert.Equal(8.0m, p3.Score);

            // Verify unclaimed applied roles tracked (only p1 was applied)
            Assert.Equal(1, context.Summary.UnclaimedAppliedPostings);
            Assert.Contains("Principal Architect", context.Summary.UnclaimedAppliedPostingTitles);

            // Verify reconciliation equation: read = created + updated + unchanged + skipped + errored
            var recon = context.Summary.FileReconciliations["postings.json"];
            Assert.Equal(3, recon.Read);
            Assert.Equal(recon.Read, recon.Created + recon.Updated + recon.Unchanged + recon.TotalSkipped + recon.Errored);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJsonStep_HtmlEntitiesDecoded_And_CultureLevelVariantsNormalized()
    {
        var tempDir = CreateTempDir();
        try
        {
            var json = """
                {
                    "fan-club-lead-arch": {
                        "company": "Fan &amp; Club Products",
                        "title": "Fan &amp; Club Products",
                        "comp": "$180,000 &ndash; $210,000",
                        "href": "https://example.com/job?id=101&amp;ref=tracker",
                        "culture": "Fast&mdash;moving team &middot; high autonomy",
                        "cultureLevel": "high-risk",
                        "loc": "Austin, TX &amp; Remote",
                        "parent": "Fan &amp; Club Holdings",
                        "note": "Candidate &quot;A&quot; feedback",
                        "status": "applied",
                        "added": "2026-03-01"
                    },
                    "second-role": {
                        "company": "Another &amp; Co",
                        "title": "Platform Lead",
                        "comp": "$220,000 &ndash; $250,000",
                        "cultureLevel": "High Risk",
                        "status": "scored",
                        "added": "2026-03-02"
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), json);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var step = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, context.Summary.PostingsCreated);
            Assert.Equal(0, context.Summary.CultureUnmapped);

            // 1. First posting: Fan & Club Products
            var p1 = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.Title == "Fan & Club Products");
            Assert.NotNull(p1);
            Assert.Equal("Fan & Club Products", p1.Title);
            Assert.Equal("Austin, TX & Remote", p1.Location);
            Assert.Equal("Fast—moving team · high autonomy", p1.CultureSummary);
            Assert.Equal(CultureLevel.HighRisk, p1.CultureLevel);
            Assert.Equal("https://example.com/job?id=101&ref=tracker", p1.OriginalUrl);
            Assert.NotNull(p1.Compensation);
            Assert.Equal(180000m, p1.Compensation.Min);
            Assert.Equal(210000m, p1.Compensation.Max);
            Assert.Equal("$180,000 – $210,000", p1.Compensation.Text);

            var c1 = inMemoryCtx.SavedCompanies.FirstOrDefault(c => c.Name == "Fan & Club Products");
            Assert.NotNull(c1);
            Assert.Equal("Fan & Club Holdings", c1.Description);

            // Dedup/matching cache has decoded company and title
            var matchKey = $"Fan & Club Products|{TitleNormalizer.Normalize("Fan & Club Products")}";
            Assert.True(context.PostingsByCompanyAndTitle.ContainsKey(matchKey));

            // 2. Second posting: High Risk cultureLevel variant normalized
            var p2 = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.Title == "Platform Lead");
            Assert.NotNull(p2);
            Assert.Equal(CultureLevel.HighRisk, p2.CultureLevel);
            Assert.NotNull(p2.Compensation);
            Assert.Equal(220000m, p2.Compensation.Min);
            Assert.Equal(250000m, p2.Compensation.Max);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJson_And_ApplicationsCsv_MatchAcrossHtmlEntities_ClaimedAndNotCreatedTwice()
    {
        var tempDir = CreateTempDir();
        try
        {
            var postingsJson = """
                {
                    "fan-club-arch": {
                        "company": "Fan &amp; Club Products",
                        "title": "Lead Architect",
                        "status": "applied",
                        "added": "2026-03-01",
                        "href": "https://example.com/job?id=101&amp;ref=tracker"
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);

            var applicationsCsv = """
                kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
                application,Fan & Club Products,Lead Architect,,2026-03-05,,,,,,,,,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), applicationsCsv);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var postingsStep = new PostingsJsonStep(inMemoryCtx, criteriaStep);
            var csvParser = new Rfc4180CsvParser();
            var applicationsStep = new ApplicationsStep(csvParser, inMemoryCtx);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            // Run postings.json step
            var postingsResult = await postingsStep.ExecuteAsync(context);
            Assert.True(postingsResult.IsSuccess);
            Assert.Equal(1, context.Summary.PostingsCreated);
            Assert.Equal(1, context.Summary.UnclaimedAppliedPostings);
            Assert.Contains("Lead Architect", context.Summary.UnclaimedAppliedPostingTitles);

            // Run applications.csv step
            var appResult = await applicationsStep.ExecuteAsync(context);
            Assert.True(appResult.IsSuccess);

            // Verify posting was claimed, NOT created twice
            Assert.Equal(1, context.Summary.PostingsCreated); // Still 1!
            Assert.Single(inMemoryCtx.SavedPostings); // Exactly 1 posting in DB
            Assert.Equal(1, context.Summary.ApplicationsCreated);
            Assert.Equal(1, context.Summary.PostingsUpdated);
            Assert.Equal(0, context.Summary.UnclaimedAppliedPostings); // Claimed!
            Assert.Empty(context.Summary.UnclaimedAppliedPostingTitles);

            var posting = inMemoryCtx.SavedPostings[0];
            Assert.Equal("Lead Architect", posting.Title);
            Assert.Equal(PostingDisposition.Applied, posting.Disposition);
            Assert.Contains(posting.Id, context.ClaimedPostingIds);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJsonStep_EmptyPresentFile_ProducesValidationErrorAndFails()
    {
        var tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), "   \r\n  ");

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var step = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
            Assert.Contains("postings.json is present but empty", result.Error!.Value.Message);

            var recon = context.Summary.FileReconciliations["postings.json"];
            Assert.Equal(1, recon.Errored);
            var err = Assert.Single(context.Summary.Errors);
            Assert.Equal("postings.json", err.SourceFile);
            Assert.Equal(ErrorCode.ValidationError.ToString(), err.ErrorCode);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJsonStep_MalformedJson_ProducesValidationErrorAndFails()
    {
        var tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), "{ invalid json: 123 ");

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var step = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);

            var recon = context.Summary.FileReconciliations["postings.json"];
            Assert.Equal(1, recon.Errored);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJsonStep_WrongJsonShape_ProducesValidationErrorAndFails()
    {
        var tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), "\"a simple string\"");

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var step = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);

            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);

            var recon = context.Summary.FileReconciliations["postings.json"];
            Assert.Equal(1, recon.Errored);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJsonStep_ArrayFallbackShape_StillImportsSuccessfully()
    {
        var tempDir = CreateTempDir();
        try
        {
            var json = """
                [
                    {
                        "slug": "legacy-slug-1",
                        "title": "Legacy Architect",
                        "company": "Legacy Corp",
                        "status": "lead",
                        "url": "https://example.com/job/legacy1"
                    }
                ]
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), json);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var step = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);

            Assert.True(result.IsSuccess);
            Assert.Equal(1, context.Summary.PostingsCreated);

            var p = Assert.Single(inMemoryCtx.SavedPostings);
            Assert.Null(p.AtsTenant);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJsonStep_AssertsAtsTenantIsNull_ForEveryImportedPosting()
    {
        var tempDir = CreateTempDir();
        try
        {
            var json = """
                {
                    "slug-one": {
                        "company": "Acme Corp",
                        "title": "Engineer Alpha",
                        "status": "lead"
                    },
                    "slug-two": {
                        "company": "Globex",
                        "title": "Engineer Beta",
                        "status": "scored"
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), json);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var step = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);
            Assert.True(result.IsSuccess);
            Assert.Equal(2, inMemoryCtx.SavedPostings.Count);
            Assert.All(inMemoryCtx.SavedPostings, posting => Assert.Null(posting.AtsTenant));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJsonStep_DifferentSlugsSameCompanyAndTitle_CountsAsOneCreatedOneUnchanged_AndRerunCreatesNothing()
    {
        var tempDir = CreateTempDir();
        try
        {
            // Two entries in postings.json with different slugs but same company and title
            var json = """
                {
                    "acme-slug-a": {
                        "company": "Acme Corp",
                        "title": "Staff Reliability Engineer",
                        "status": "scored",
                        "score": 8.0
                    },
                    "acme-slug-b": {
                        "company": "Acme Corp",
                        "title": "Staff Reliability Engineer",
                        "status": "scored",
                        "score": 8.5
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), json);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var step = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            // First run
            var context1 = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result1 = await step.ExecuteAsync(context1);
            Assert.True(result1.IsSuccess);
            Assert.Equal(1, context1.Summary.PostingsCreated);
            Assert.Equal(1, context1.Summary.PostingsUnchanged);
            var recon1 = context1.Summary.FileReconciliations["postings.json"];
            Assert.Equal(2, recon1.Read);
            Assert.Equal(1, recon1.Created);
            Assert.Equal(1, recon1.Unchanged);
            Assert.Single(inMemoryCtx.SavedPostings);

            // Second run (re-run of the same bundle)
            var context2 = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result2 = await step.ExecuteAsync(context2);
            Assert.True(result2.IsSuccess);
            Assert.Equal(0, context2.Summary.PostingsCreated);
            Assert.Equal(2, context2.Summary.PostingsUnchanged);
            var recon2 = context2.Summary.FileReconciliations["postings.json"];
            Assert.Equal(2, recon2.Read);
            Assert.Equal(0, recon2.Created);
            Assert.Equal(2, recon2.Unchanged);
            Assert.Single(inMemoryCtx.SavedPostings); // Still only one posting in storage
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AllSteps_PresentEmptyFiles_ProduceValidationError()
    {
        var tempDir = CreateTempDir();
        try
        {
            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var parser = new Rfc4180CsvParser();

            // 1. company-aliases.csv empty
            File.WriteAllText(Path.Combine(tempDir, "company-aliases.csv"), "   ");
            var aliasesStep = new CompanyAliasesStep(parser);
            var resAliases = await aliasesStep.ExecuteAsync(new TrackerImportContext { OwnerId = TestOwnerId, BundleDirectory = tempDir });
            Assert.False(resAliases.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, resAliases.Error!.Value.Code);

            // 2. watchlist-companies.csv empty
            File.WriteAllText(Path.Combine(tempDir, "watchlist-companies.csv"), "   ");
            var watchlistStep = new WatchlistCompaniesStep(parser, inMemoryCtx);
            var resWatchlist = await watchlistStep.ExecuteAsync(new TrackerImportContext { OwnerId = TestOwnerId, BundleDirectory = tempDir });
            Assert.False(resWatchlist.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, resWatchlist.Error!.Value.Code);

            // 3. appendix-rows.csv empty
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), "   ");
            var appendixStep = new AppendixRowsStep(parser, inMemoryCtx, criteriaStep);
            var resAppendix = await appendixStep.ExecuteAsync(new TrackerImportContext { OwnerId = TestOwnerId, BundleDirectory = tempDir });
            Assert.False(resAppendix.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, resAppendix.Error!.Value.Code);

            // 4. applications.csv empty
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), "   ");
            var appStep = new ApplicationsStep(parser, inMemoryCtx);
            var resApp = await appStep.ExecuteAsync(new TrackerImportContext { OwnerId = TestOwnerId, BundleDirectory = tempDir });
            Assert.False(resApp.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, resApp.Error!.Value.Code);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AppendixRowsStep_ProcessesNonDuplicatesBeforeDuplicates_AndLinksDuplicateOfId()
    {
        var tempDir = CreateTempDir();
        try
        {
            var csv = """
                source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes
                aggregator,duplicate,Acme Corp,Software Engineer,-12822,,,yes,Details,2026-05-01,duplicate,duplicate,never,false,,,,
                aggregator,screened_out,Acme Corp,Software Engineer,J28518,,,yes,Details,2026-05-01,no-remote,no-remote,never,false,,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), csv);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var parser = new Rfc4180CsvParser();
            var step = new AppendixRowsStep(parser, inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, context.Summary.PostingsCreated);

            var orig = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.RequisitionId == "J28518");
            var dup = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.RequisitionId == "-12822");

            Assert.NotNull(orig);
            Assert.NotNull(dup);
            Assert.Equal(orig.Id, dup.DuplicateOfId);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AppendixRowsStep_FileReconciliation_SatisfiesReadEqualsCreatedPlusUpdatedPlusUnchangedPlusSkippedPlusErrored()
    {
        var tempDir = CreateTempDir();
        try
        {
            var csv = """
                source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes
                aggregator,screened_out,Acme Corp,Software Engineer,REQ-1,,,yes,Details,2026-05-01,no-remote,no-remote,never,false,,,,
                aggregator,screened_out,Acme Corp,Skipped Engineer,REQ-2,,,yes,Details,2026-05-01,no-remote,no-remote,never,false,,,skip,
                aggregator,screened_out,Acme Corp,,REQ-3,,,yes,Details,2026-05-01,no-remote,no-remote,never,false,,,,
                ,,,,,,,,,,,,,,,,,
                aggregator,screened_out,Acme Corp,Software Engineer,REQ-1,,,yes,Details,2026-05-01,no-remote,no-remote,never,false,,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), csv);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var parser = new Rfc4180CsvParser();
            var step = new AppendixRowsStep(parser, inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);
            Assert.True(result.IsSuccess);

            var recon = context.Summary.FileReconciliations["appendix-rows.csv"];
            Assert.Equal(5, recon.Read);
            Assert.Equal(1, recon.Created);
            Assert.Equal(1, recon.Unchanged);
            Assert.Equal(2, recon.TotalSkipped);
            Assert.Equal(1, recon.Errored);

            Assert.Equal(recon.Read, recon.Created + recon.Updated + recon.Unchanged + recon.TotalSkipped + recon.Errored);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJsonStep_GhostSignals_Parsing_And_Idempotency()
    {
        var tempDir = CreateTempDir();
        try
        {
            var json = """
                {
                    "slug-1": {
                        "title": "Engineer Parallel",
                        "company": "Acme Corp",
                        "ghost": "parallel"
                    },
                    "slug-2": {
                        "title": "Engineer Repost NoSalary",
                        "company": "Acme Corp",
                        "ghost": "repost nosalary"
                    },
                    "slug-3": {
                        "title": "Engineer Phantom Deadline",
                        "company": "Acme Corp",
                        "ghost": "phantom deadline:2026-05-25"
                    },
                    "slug-4": {
                        "title": "Engineer Gone Invalid And Unknown",
                        "company": "Acme Corp",
                        "ghost": "gone deadline:invalid_date unknown_xyz"
                    },
                    "slug-5": {
                        "title": "Engineer Bool Ghost",
                        "company": "Acme Corp",
                        "ghost": true
                    },
                    "slug-6": {
                        "title": "Engineer No Ghost",
                        "company": "Acme Corp",
                        "ghost": false
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), json);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var step = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);
            Assert.True(result.IsSuccess);

            // Parallel (1) + Repost/NoSalary (2) + Phantom/Deadline (2) + Gone (1) + Bool Phantom (1) = 7 signals
            Assert.Equal(7, context.Summary.SignalsCreated);
            Assert.Equal(7, inMemoryCtx.SavedSignals.Count);

            // Invalid date and unknown token recorded as unmapped
            Assert.True(context.Summary.UnmappedValues.TryGetValue("ghost", out var unmappedCount));
            Assert.Equal(2, unmappedCount);

            // Verify specific signals
            var p3 = inMemoryCtx.SavedPostings.First(p => p.Title == "Engineer Phantom Deadline");
            var p3Signals = inMemoryCtx.SavedSignals.Where(s => s.PostingId == p3.Id).ToList();
            Assert.Equal(2, p3Signals.Count);
            Assert.Contains(p3Signals, s => s.SignalType == SignalType.Phantom);
            Assert.Contains(p3Signals, s => s.SignalType == SignalType.Deadline && s.DeadlineDate == "2026-05-25");

            // Re-run the step on the exact same bundle
            var rerunContext = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var rerunResult = await step.ExecuteAsync(rerunContext);
            Assert.True(rerunResult.IsSuccess);

            // Zero signals created on re-run, no duplicates
            Assert.Equal(0, rerunContext.Summary.SignalsCreated);
            Assert.Equal(7, inMemoryCtx.SavedSignals.Count);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AppendixRowsStep_ProposedDisposition_And_Resurface_Mapping()
    {
        var tempDir = CreateTempDir();
        try
        {
            var csv = """
                source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes
                aggregator,screened_out,Acme Corp,Screened Role,J28518,,,yes,Details,05/01,reason-screened,reason-screened,never,false,,,,
                aggregator,closed,Acme Corp,Closed Role,-12822,,,yes,Details,05/02,reason-closed,reason-closed,if_reopened,false,,,,
                aggregator,unscored,Acme Corp,Unscored Role,R-20260720,,,yes,Details,05/03,reason-unscored,reason-unscored,,false,,,,
                aggregator,dismissed,Acme Corp,Dismissed Role,JR-000829,,,yes,Details,05/04,,dismissed,,false,,,,
                aggregator,duplicate,Acme Corp,Screened Role,19505,,,yes,Details,05/05,duplicate,duplicate,,false,,,,
                aggregator,invalid_disposition,Acme Corp,Invalid Role,20261,,,yes,Details,05/06,bad,bad,,false,,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), csv);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var parser = new Rfc4180CsvParser();
            var step = new AppendixRowsStep(parser, inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);
            Assert.True(result.IsSuccess);

            // 5 created (screened_out, closed, unscored, dismissed, duplicate), 1 errored (invalid_disposition)
            var recon = context.Summary.FileReconciliations["appendix-rows.csv"];
            Assert.Equal(6, recon.Read);
            Assert.Equal(5, recon.Created);
            Assert.Equal(1, recon.Errored);

            var pScreened = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "J28518");
            Assert.Equal(PostingDisposition.ScreenedOut, pScreened.Disposition);
            Assert.Equal(ResurfacePolicy.Never, pScreened.ResurfacePolicy);
            Assert.Null(pScreened.ClosedDate);

            var pClosed = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "-12822");
            Assert.Equal(PostingDisposition.Closed, pClosed.Disposition);
            Assert.Equal(ResurfacePolicy.IfReopened, pClosed.ResurfacePolicy);
            Assert.NotNull(pClosed.ClosedDate);

            var pUnscored = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "R-20260720");
            Assert.Equal(PostingDisposition.Unscored, pUnscored.Disposition);
            // Blank resurface policy leaves domain default (Never)
            Assert.Equal(ResurfacePolicy.Never, pUnscored.ResurfacePolicy);
            Assert.Null(pUnscored.ClosedDate);

            var pDismissed = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "JR-000829");
            Assert.Equal(PostingDisposition.ScreenedOut, pDismissed.Disposition);
            Assert.Null(pDismissed.ClosedDate);

            var pDup = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "19505");
            Assert.Equal(pScreened.Id, pDup.DuplicateOfId);
            Assert.Equal(PostingDisposition.ScreenedOut, pDup.Disposition);

            // CriteriaCreated was incremented dynamically when reason criteria were seeded
            Assert.True(context.Summary.CriteriaCreated > 0);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AppendixRowsStep_MissingOrMisspelledHeader_FailsUpfrontWithValidationError()
    {
        var tempDir = CreateTempDir();
        try
        {
            // Missing comp_detected column
            var badCsv = """
                source,proposed_disposition,company,title,req,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes
                aggregator,screened_out,Acme Corp,Engineer,REQ-1,,true,Raw,2026-05-01,no-remote,no-remote,never,false,,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), badCsv);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var parser = new Rfc4180CsvParser();
            var step = new AppendixRowsStep(parser, inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
            Assert.Contains("appendix-rows.csv", result.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("comp_detected", result.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ApplicationsStep_MissingOrMisspelledHeader_FailsUpfrontWithValidationError()
    {
        var tempDir = CreateTempDir();
        try
        {
            // Misspelled desired_salary as target_salary
            var badCsv = """
                kind,company,title,req,applied_on,rejected,rejected_date_md,target_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
                application,Acme Corp,Engineer,REQ-1,2026-05-01,false,,200000,false,good,tailored,,,raw,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), badCsv);

            var inMemoryCtx = new InMemoryPostingContext();
            var parser = new Rfc4180CsvParser();
            var step = new ApplicationsStep(parser, inMemoryCtx);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);
            Assert.False(result.IsSuccess);
            Assert.Equal(ErrorCode.ValidationError, result.Error!.Value.Code);
            Assert.Contains("applications.csv", result.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("desired_salary", result.Error.Value.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AppendixRowsStep_RealShapedReqValues_DeduplicationAndDedupKeyC()
    {
        var tempDir = CreateTempDir();
        try
        {
            // Exercises real-shaped req values: J28518, -12822, R-20260720
            // Two rows with same company & title but different req (J28518 and -12822) create 2 postings
            // Same req twice (R-20260720 twice) creates 1 posting and 1 unchanged (merged)
            var csv = """
                source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes
                aggregator,screened_out,Real Corp,Staff Architect,J28518,,,yes,Details,2026-05-01,no-remote,no-remote,never,false,,,,
                aggregator,screened_out,Real Corp,Staff Architect,-12822,,,yes,Details,2026-05-01,no-remote,no-remote,never,false,,,,
                aggregator,screened_out,Real Corp,Principal Engineer,R-20260720,,,yes,Details,2026-05-02,no-remote,no-remote,never,false,,,,
                aggregator,screened_out,Real Corp,Principal Engineer,R-20260720,,,yes,Details,2026-05-02,no-remote,no-remote,never,false,,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), csv);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var parser = new Rfc4180CsvParser();
            var step = new AppendixRowsStep(parser, inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);
            Assert.True(result.IsSuccess);

            // 3 postings created (J28518, -12822, and first R-20260720), 1 unchanged (merged second R-20260720)
            Assert.Equal(3, context.Summary.PostingsCreated);
            Assert.Equal(1, context.Summary.PostingsUnchanged);

            var p1 = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.RequisitionId == "J28518");
            var p2 = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.RequisitionId == "-12822");
            var p3 = inMemoryCtx.SavedPostings.FirstOrDefault(p => p.RequisitionId == "R-20260720");

            Assert.NotNull(p1);
            Assert.NotNull(p2);
            Assert.NotNull(p3);

            // Verify they are distinct postings
            Assert.NotEqual(p1.Id, p2.Id);
            Assert.Equal("J28518", p1.RequisitionId);
            Assert.Equal("-12822", p2.RequisitionId);
            Assert.Equal("R-20260720", p3.RequisitionId);

            // Verify dedup tier C format: $"{CompanyId:D}|{RequisitionId}"
            Assert.Equal($"{p1.CompanyId:D}|J28518", p1.DedupKeyC);
            Assert.Equal($"{p2.CompanyId:D}|-12822", p2.DedupKeyC);
            Assert.Equal($"{p3.CompanyId:D}|R-20260720", p3.DedupKeyC);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AppendixRowsStep_CompensationAndRemoteMentioned_ParsedAndStored()
    {
        var tempDir = CreateTempDir();
        try
        {
            // Row 1: comp range with estimate ("$203.6k-$366.4k", comp_is_estimate "yes")
            // Row 2: single comp value ("$237k")
            // Row 3: remote_mentioned "yes"
            var csv = """
                source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes
                aggregator,screened_out,Comp Corp,Range Role,J28518,"$203.6k-$366.4k",yes,no,Details,2026-05-01,reason,reason,never,false,,,,
                aggregator,screened_out,Comp Corp,Single Role,-12822,"$237k",no,no,Details,2026-05-02,reason,reason,never,false,,,,
                aggregator,screened_out,Comp Corp,Remote Role,R-20260720,,,yes,Details,2026-05-03,reason,reason,never,false,,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), csv);

            var inMemoryCtx = new InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var parser = new Rfc4180CsvParser();
            var step = new AppendixRowsStep(parser, inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);
            Assert.True(result.IsSuccess);
            Assert.Equal(3, context.Summary.PostingsCreated);

            var pRange = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "J28518");
            Assert.NotNull(pRange.Compensation);
            Assert.Equal(203600m, pRange.Compensation.Min);
            Assert.Equal(366400m, pRange.Compensation.Max);
            Assert.Contains("estimate", pRange.Compensation.Text, StringComparison.OrdinalIgnoreCase);

            var pSingle = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "-12822");
            Assert.NotNull(pSingle.Compensation);
            Assert.Equal(237000m, pSingle.Compensation.Min);
            Assert.Null(pSingle.Compensation.Max);

            var pRemote = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "R-20260720");
            Assert.Equal(RemoteType.Unknown, pRemote.RemoteType);
            Assert.Null(pRemote.CultureSummary);
            Assert.Equal(1, context.Summary.UnmappedValues["remote_mentioned"]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ApplicationsStep_FitAndReferral_MappedToEventNotes_AndResumeVariantsDefaulted()
    {
        var tempDir = CreateTempDir();
        try
        {
            // Row 1: fit="6.0.", referral="true", blank resume_variant
            var csv = """
                kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
                application,App Note Corp,Lead Dev,J28518,2026-04-10,false,,195000,true,6.0.,,,,Applied via website,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), csv);

            var inMemoryCtx = new InMemoryPostingContext();
            var parser = new Rfc4180CsvParser();
            var step = new ApplicationsStep(parser, inMemoryCtx);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await step.ExecuteAsync(context);
            Assert.True(result.IsSuccess);

            // Mapped to event notes: 2 (1 for fit: 6.0, 1 for referral=true)
            Assert.Equal(2, context.Summary.MappedToEventNotes);

            // Resume variants defaulted: 1 (blank defaulted to AsIs)
            Assert.Equal(1, context.Summary.ResumeVariantsDefaulted);

            // Assert applied event notes
            Assert.Single(inMemoryCtx.SavedEvents);
            var appliedEvent = inMemoryCtx.SavedEvents[0];
            Assert.Equal(ApplicationEventType.Applied, appliedEvent.EventType);
            Assert.NotNull(appliedEvent.Notes);
            Assert.Contains("fit: 6.0", appliedEvent.Notes);
            Assert.Contains("referral=true", appliedEvent.Notes);
            Assert.Contains("resume variant unspecified", appliedEvent.Notes);

            // Assert application entity has ResumeVariant.AsIs
            Assert.Single(inMemoryCtx.SavedApplications);
            var app = inMemoryCtx.SavedApplications[0];
            Assert.Equal(ResumeVariant.AsIs, app.ResumeVariant);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task TrackerImportPipeline_DispositionHistogram_TotalEqualsPostingsCreatedPlusExisting()
    {
        var tempDir = CreateTempDir();
        try
        {
            // Bundle has:
            // 1 posting in postings.json (scored)
            // 2 postings in appendix-rows.csv (1 screened_out, 1 duplicate linked with duplicate_of_id)
            var postingsJson = """
                {
                  "hist-lead-arch": {
                    "company": "Hist Corp",
                    "title": "Lead Architect",
                    "status": "scored",
                    "href": "https://example.com/job/hist1"
                  }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);

            var appendixCsv = """
                source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes
                aggregator,screened_out,Hist Corp,Platform Eng,REQ-H1,,,yes,Details,2026-05-01,reason,reason,never,false,,,,
                aggregator,duplicate,Hist Corp,Platform Eng,REQ-H2,,,yes,Details,2026-05-02,duplicate,duplicate,never,false,,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), appendixCsv);

            var inMemoryCtx = new InMemoryPostingContext();

            // Pre-seed 2 existing postings in repository with different dispositions
            var existing1 = Posting.Create(
                ownerId: TestOwnerId,
                companyId: Guid.NewGuid(),
                title: "Existing Rejected Role",
                sourceKind: SourceKind.Ats,
                disposition: PostingDisposition.Rejected);
            inMemoryCtx.SavedPostings.Add(existing1);

            var existing2 = Posting.Create(
                ownerId: TestOwnerId,
                companyId: Guid.NewGuid(),
                title: "Existing Closed Role",
                sourceKind: SourceKind.Ats,
                disposition: PostingDisposition.Closed);
            inMemoryCtx.SavedPostings.Add(existing2);

            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var csvParser = new Rfc4180CsvParser();
            var pipeline = new TrackerImportPipeline(
                steps: new ITrackerImportStep[]
                {
                    new CompanyAliasesStep(csvParser),
                    new WatchlistCompaniesStep(csvParser, inMemoryCtx),
                    new PostingsJsonStep(inMemoryCtx, criteriaStep),
                    new AppendixRowsStep(csvParser, inMemoryCtx, criteriaStep),
                    new ApplicationsStep(csvParser, inMemoryCtx)
                },
                trackingContext: inMemoryCtx);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var result = await pipeline.ExecuteAsync(context);
            Assert.True(result.IsSuccess);

            var summary = result.Value!;
            var existingCount = 2;

            // Total created during run = 1 (postings.json) + 2 (appendix-rows including duplicate) = 3
            Assert.Equal(3, summary.PostingsCreated);

            // Total in histogram must equal Postings Created + existing postings = 3 + 2 = 5
            var histogramTotal = summary.DispositionHistogram.Values.Sum();
            Assert.Equal(summary.PostingsCreated + existingCount, histogramTotal);
            Assert.Equal(5, histogramTotal);

            // Assert individual dispositions are counted
            Assert.True(summary.DispositionHistogram.ContainsKey("scored"));
            Assert.True(summary.DispositionHistogram.ContainsKey("screened_out"));
            Assert.True(summary.DispositionHistogram.ContainsKey("rejected"));
            Assert.True(summary.DispositionHistogram.ContainsKey("closed"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rr_post_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    internal sealed class InMemoryPostingContext : ITrackingTransactionContext
    {
        public List<Company> SavedCompanies { get; } = new();
        public List<Posting> SavedPostings { get; } = new();
        public List<PostingSignal> SavedSignals { get; } = new();
        public List<Criterion> SavedCriteria { get; } = new();
        public List<AuditLogEntry> AppendedAuditEntries { get; } = new();
        public List<RoleRadar.Domain.Tracking.Application> SavedApplications { get; } = new();
        public List<ApplicationEvent> SavedEvents { get; } = new();
        public List<Note> SavedNotes { get; } = new();

        public ICompanyRepository Companies => new FakeCompanyRepository(SavedCompanies);
        public IPostingRepository Postings => new FakePostingRepository(SavedPostings, SavedSignals);
        public ICriterionRepository Criteria => new FakeCriterionRepository(SavedCriteria);
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

        public Task<Result<Guid?>> GetNoteIdByTitleAndAreaAsync(string ownerId, string title, string area, CancellationToken cancellationToken = default)
        {
            var note = SavedNotes.FirstOrDefault(n => n.OwnerId == ownerId && n.Title == title && n.Area == area);
            return Task.FromResult(Result<Guid?>.Success(note?.Id));
        }

        private sealed class FakeCompanyRepository : ICompanyRepository
        {
            private readonly List<Company> _companies;
            public FakeCompanyRepository(List<Company> companies) => _companies = companies;
            public Task<Result<Company?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Company?>.Success(_companies.FirstOrDefault(c => c.Id == id)));
            public Task<Result<Company?>> GetByNormalizedNameAsync(string ownerId, string normalizedName, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Company?>.Success(_companies.FirstOrDefault(c => string.Equals(c.NormalizedName, normalizedName.Trim(), StringComparison.OrdinalIgnoreCase))));
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
            private readonly List<PostingSignal> _signals;
            public FakePostingRepository(List<Posting> postings, List<PostingSignal> signals)
            {
                _postings = postings;
                _signals = signals;
            }
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
            public Task<Result<bool>> AppendSignalAsync(PostingSignal signal, CancellationToken cancellationToken = default)
            {
                _signals.Add(signal);
                return Task.FromResult(Result<bool>.Success(true));
            }
            public Task<Result<IReadOnlyList<PostingSignal>>> GetSignalsAsync(string ownerId, Guid postingId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<PostingSignal>>.Success(_signals.Where(s => s.PostingId == postingId).ToList()));
            public Task<Result<ILookup<Guid, PostingSignal>>> GetSignalsForPostingsAsync(string ownerId, IEnumerable<Guid> postingIds, CancellationToken cancellationToken = default) =>
                throw new NotImplementedException();
        }

        private sealed class FakeCriterionRepository : ICriterionRepository
        {
            private readonly List<Criterion> _criteria;
            public FakeCriterionRepository(List<Criterion> criteria) => _criteria = criteria;
            public Task<Result<Criterion?>> GetActiveByKeyAsync(string ownerId, string key, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Criterion?>.Success(_criteria.FirstOrDefault(c => c.Key == key)));
            public Task<Result<Criterion?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<Criterion?>.Success(_criteria.FirstOrDefault(c => c.Id == id)));
            public Task<Result<IReadOnlyList<Criterion>>> GetEffectiveAtAsync(string ownerId, DateTimeOffset effectiveDate, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<Criterion>>.Success(_criteria));
            public Task<Result<IReadOnlyList<Criterion>>> ListActiveAsync(string ownerId, CancellationToken cancellationToken = default) =>
                Task.FromResult(Result<IReadOnlyList<Criterion>>.Success(_criteria));
            public Task<Result<bool>> UpsertCriterionAsync(Criterion criterion, CancellationToken cancellationToken = default)
            {
                var index = _criteria.FindIndex(c => c.Id == criterion.Id);
                if (index >= 0) _criteria[index] = criterion;
                else _criteria.Add(criterion);
                return Task.FromResult(Result<bool>.Success(true));
            }
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
