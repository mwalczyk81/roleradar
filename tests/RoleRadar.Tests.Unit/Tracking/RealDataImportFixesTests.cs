using System.Globalization;
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

public sealed class RealDataImportFixesTests
{
    private const string TestOwnerId = "usr_0192e401000070008000000000000001";
    private static readonly DateOnly TestDefaultDate = new(2026, 4, 15);

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "rr_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public async Task FirstRowWins_ThreeRowsAndMerge_ClosedStaysClosedAndNoUnintendedDuplicate()
    {
        // Requirement B.4:
        // Test with three rows:
        // Row 1: closed with req J28518
        // Row 2: screened_out with req -12822 (same company+title)
        // Row 3: screened_out with no req and req text in title ("Lead Architect J28518")
        // Row 4: screened_out with same company+title and same req J28518 (later merge row)
        // Assert: Row 1 stays closed, all three distinct exist, no unintended duplicate_of_id, Row 4 is unchanged (merged).
        var tempDir = CreateTempDir();
        try
        {
            var csv = """
                source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes
                aggregator,closed,Contoso Ltd,Lead Architect,J28518,,,yes,Closed details,2026-05-01,closed-role,closed-role,if_reopened,false,,,,
                aggregator,screened_out,Contoso Ltd,Lead Architect,-12822,,,yes,Screened details,2026-05-02,no-match,no-match,never,false,,,,
                aggregator,screened_out,Contoso Ltd,Lead Architect J28518,,,,yes,Title req details,2026-05-03,comp,comp,never,false,,,,
                aggregator,screened_out,Contoso Ltd,Lead Architect,J28518,,,yes,Later overwrite attempt,2026-05-04,screened-late,screened-late,never,false,,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), csv);

            var inMemoryCtx = new PostingImportStepTests.InMemoryPostingContext();
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

            // Reconcile: 4 read: 3 created, 1 unchanged (merged)
            var recon = context.Summary.FileReconciliations["appendix-rows.csv"];
            Assert.Equal(4, recon.Read);
            Assert.Equal(3, recon.Created);
            Assert.Equal(1, recon.Unchanged);
            Assert.Equal(1, recon.UnchangedByReason["merged"]);

            Assert.Equal(3, inMemoryCtx.SavedPostings.Count);

            // Check Row 1: closed stays closed with policy IfReopened
            var p1 = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "J28518");
            Assert.Equal(PostingDisposition.Closed, p1.Disposition);
            Assert.Equal(ResurfacePolicy.IfReopened, p1.ResurfacePolicy);
            Assert.NotNull(p1.ClosedDate);
            Assert.Null(p1.DuplicateOfId);

            // Check Row 2: screened out with req -12822
            var p2 = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "-12822");
            Assert.Equal(PostingDisposition.ScreenedOut, p2.Disposition);
            Assert.Null(p2.DuplicateOfId);

            // Check Row 3: screened out with no req
            var p3 = inMemoryCtx.SavedPostings.First(p => p.Title == "Lead Architect J28518");
            Assert.Null(p3.RequisitionId);
            Assert.Equal(PostingDisposition.ScreenedOut, p3.Disposition);
            Assert.Null(p3.DuplicateOfId);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ConflictingApplications_SecondRowFailsWithValidationError()
    {
        // Requirement B.5:
        // If two different application rows resolve to the same posting, the second is a row ValidationError (conflicting application), not "unchanged".
        // "Unchanged" is only for identical re-run rows.
        var tempDir = CreateTempDir();
        try
        {
            var csv = """
                kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
                application,Acme Corp,Principal Engineer,J28518,2026-03-10,false,,200000,false,strong_fit,standard,,,App 1,,,
                application,Acme Corp,Principal Engineer,J28518,2026-03-15,false,,210000,false,strong_fit,tailored,,,App 2 conflicting,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), csv);

            var inMemoryCtx = new PostingImportStepTests.InMemoryPostingContext();
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

            // 1 created, 1 errored with ValidationError
            var recon = context.Summary.FileReconciliations["applications.csv"];
            Assert.Equal(2, recon.Read);
            Assert.Equal(1, recon.Created);
            Assert.Equal(1, recon.Errored);
            Assert.Equal(0, recon.Unchanged);

            var error = Assert.Single(context.Summary.Errors);
            Assert.Equal("applications.csv", error.SourceFile);
            Assert.Equal(3, error.LineNumber);
            Assert.Equal(ErrorCode.ValidationError.ToString(), error.ErrorCode);
            Assert.Contains("Conflicting application", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task TwoCompaniesSharingReqString_ResolveIndependentlyAndBothReject()
    {
        // Requirement A.1 & B.6:
        // Scope every requisition index by company: key = (companyId, req), never req alone.
        // Two companies sharing a req string with one rejected application each:
        // Leaves both postings in disposition rejected and application outcomes in rejected.
        var tempDir = CreateTempDir();
        try
        {
            var csv = """
                kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
                application,Alpha Corp,Staff Engineer,J28518,2026-03-10,yes,2026-03-20,200000,,,,,,Rejected from Alpha,,
                application,Beta Corp,Staff Engineer,J28518,2026-03-12,yes,2026-03-22,220000,,,,,,Rejected from Beta,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), csv);

            var inMemoryCtx = new PostingImportStepTests.InMemoryPostingContext();
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

            var recon = context.Summary.FileReconciliations["applications.csv"];
            Assert.Equal(2, recon.Read);
            Assert.Equal(2, recon.Created);
            Assert.Equal(0, recon.Errored);

            // Assert two distinct postings exist under separate companies
            Assert.Equal(2, inMemoryCtx.SavedPostings.Count);
            var pAlpha = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "J28518" && p.CompanyId == inMemoryCtx.SavedCompanies.First(c => c.Name == "Alpha Corp").Id);
            var pBeta = inMemoryCtx.SavedPostings.First(p => p.RequisitionId == "J28518" && p.CompanyId == inMemoryCtx.SavedCompanies.First(c => c.Name == "Beta Corp").Id);

            Assert.NotEqual(pAlpha.CompanyId, pBeta.CompanyId);
            Assert.Equal(PostingDisposition.Rejected, pAlpha.Disposition);
            Assert.Equal(PostingDisposition.Rejected, pBeta.Disposition);

            // Assert both applications have outcome Rejected
            Assert.Equal(2, inMemoryCtx.SavedApplications.Count);
            Assert.All(inMemoryCtx.SavedApplications, app => Assert.Equal(ApplicationOutcome.Rejected, app.Outcome));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJson_Note_CreatesScreeningNoteAndAttachesScreeningNoteId_IdempotentOnRerun()
    {
        // Requirement C.7:
        // postings.json "note": create one note per posting through import note path:
        // area "job-search", tag "imported", title "Assessment: <company> - <title>", content = decoded note text
        // and set posting's screening_note_id. Idempotent on re-run (same title+area is not recreated).
        var tempDir = CreateTempDir();
        try
        {
            var json = """
                {
                  "test-slug-1": {
                    "company": "Gamma Inc",
                    "title": "Cloud Architect",
                    "status": "scored",
                    "score": 8.5,
                    "added": "2026-03-01",
                    "note": "Demonstrated strong cloud governance knowledge &amp; security posture."
                  }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), json);

            var inMemoryCtx = new PostingImportStepTests.InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var step = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context1 = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var run1 = await step.ExecuteAsync(context1);
            Assert.True(run1.IsSuccess);
            Assert.Equal(1, context1.Summary.PostingsCreated);
            Assert.Equal(1, context1.Summary.ScreeningNotesCreated);

            Assert.Single(inMemoryCtx.SavedNotes);
            var note = inMemoryCtx.SavedNotes[0];
            Assert.Equal("Assessment: Gamma Inc - Cloud Architect", note.Title);
            Assert.Equal("job-search", note.Area);
            Assert.Contains("imported", note.Tags);
            Assert.Equal("Demonstrated strong cloud governance knowledge & security posture.", note.Body);

            Assert.Single(inMemoryCtx.SavedPostings);
            var posting = inMemoryCtx.SavedPostings[0];
            Assert.Equal(note.Id, posting.ScreeningNoteId);

            // Re-run with the same bundle: idempotent no-op
            var context2 = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var run2 = await step.ExecuteAsync(context2);
            Assert.True(run2.IsSuccess);
            Assert.Equal(0, context2.Summary.PostingsCreated);
            Assert.Equal(1, context2.Summary.PostingsUnchanged);
            Assert.Equal(0, context2.Summary.ScreeningNotesCreated);
            Assert.True(context2.Summary.IsIdempotentNoOp);
            Assert.Single(inMemoryCtx.SavedNotes); // Still exactly 1 note
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Appendix_DispositionReasonEnrichment_CombinesFieldsAndLimitsTo500()
    {
        // Requirement C.8:
        // disposition_reason (max 500) = reason_text when present, then " | details: <details_raw>" and " | also: <reason_all codes other than the primary>"
        // while it fits (truncate at 500, count it); fallback to reason_primary when reason_text is blank.
        // Keep disposition_criterion_id from reason_primary.
        var tempDir = CreateTempDir();
        try
        {
            var longDetails = new string('x', 600);
            var csv = $$"""
                source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes
                aggregator,screened_out,Delta Tech,Platform Dev,J28518,,,yes,{{longDetails}},2026-05-01,comp,comp; commute; tech-stack,never,false,,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), csv);

            var inMemoryCtx = new PostingImportStepTests.InMemoryPostingContext();
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

            var posting = Assert.Single(inMemoryCtx.SavedPostings);
            Assert.NotNull(posting.DispositionReason);
            Assert.Equal(500, posting.DispositionReason.Length);
            Assert.Contains("details: ", posting.DispositionReason, StringComparison.Ordinal);
            Assert.Equal(1, context.Summary.FieldTruncations["disposition_reason"]);

            // disposition_criterion_id is seeded from reason_primary ("comp"), not the 500 char string
            Assert.NotNull(posting.DispositionCriterionId);
            var criterion = Assert.Single(inMemoryCtx.SavedCriteria);
            Assert.Equal("comp", criterion.Key);
            Assert.Equal(criterion.Id, posting.DispositionCriterionId);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task Applications_DesiredSalaryRange_ParsesMinMaxAndCountsUnparseable()
    {
        // Requirement C.9:
        // ranges like "$200k-$220k" or "$175k-$190k": parse to desired_salary min/max (USD).
        // Unparseable text goes into applied event notes and is counted under unmapped values ("desired_salary").
        var tempDir = CreateTempDir();
        try
        {
            var csv = """
                kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
                application,Epsilon Inc,Staff Eng,J28518,2026-03-10,false,,"$200k-$220k",,,,,,,,
                application,Zeta Inc,Staff Eng,-12822,2026-03-11,false,,"$175k-$190k",,,,,,,,
                application,Eta Inc,Staff Eng,R-20260720,2026-03-12,false,,competitive,,,,,,,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), csv);

            var inMemoryCtx = new PostingImportStepTests.InMemoryPostingContext();
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

            Assert.Equal(3, inMemoryCtx.SavedApplications.Count);

            var app1 = inMemoryCtx.SavedApplications[0];
            Assert.NotNull(app1.DesiredSalary);
            Assert.Equal(200000m, app1.DesiredSalary.Min);
            Assert.Equal(220000m, app1.DesiredSalary.Max);

            var app2 = inMemoryCtx.SavedApplications[1];
            Assert.NotNull(app2.DesiredSalary);
            Assert.Equal(175000m, app2.DesiredSalary.Min);
            Assert.Equal(190000m, app2.DesiredSalary.Max);

            var app3 = inMemoryCtx.SavedApplications[2];
            Assert.NotNull(app3.DesiredSalary);
            Assert.Null(app3.DesiredSalary.Min);
            Assert.Null(app3.DesiredSalary.Max);
            Assert.Equal(1, context.Summary.UnmappedValues["desired_salary"]);

            // Event note has "desired_salary: competitive"
            var event3 = inMemoryCtx.SavedEvents.First(e => e.ApplicationId == app3.Id);
            Assert.Contains("desired_salary: competitive", event3.Notes);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SourceFieldCoverage_PopulatesCountsForNonEmptyFields()
    {
        // Requirement D.10:
        // Verify source field coverage captures non-empty fields correctly
        var tempDir = CreateTempDir();
        try
        {
            var csv = """
                source,proposed_disposition,company,title,req,comp_detected,comp_is_estimate,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,review_flags,reason_text,decision,corrected_notes
                aggregator,screened_out,Theta LLC,Developer,J28518,$180k,yes,yes,Some details,2026-05-01,comp,comp,never,true,flag,reason text,,
                """;
            File.WriteAllText(Path.Combine(tempDir, "appendix-rows.csv"), csv);

            var inMemoryCtx = new PostingImportStepTests.InMemoryPostingContext();
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

            Assert.Equal(1, context.Summary.GetSourceFieldNonEmptyCount("appendix-rows.csv", "company"));
            Assert.Equal(1, context.Summary.GetSourceFieldNonEmptyCount("appendix-rows.csv", "title"));
            Assert.Equal(1, context.Summary.GetSourceFieldNonEmptyCount("appendix-rows.csv", "req"));
            Assert.Equal(1, context.Summary.GetSourceFieldNonEmptyCount("appendix-rows.csv", "details_raw"));
            Assert.Equal(0, context.Summary.GetSourceFieldNonEmptyCount("appendix-rows.csv", "decision"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ImporterTextHygiene_ConvertsFormattingTagsToMarkdown_StripsOthers_MatchesCleanedTitle()
    {
        var tempDir = CreateTempDir();
        try
        {
            var postingsJson = """
                {
                    "role-1": {
                        "company": "Acme Corp",
                        "title": "<b>Staff</b> Software Engineer",
                        "culture": "<strong>High</strong> trust<br>Collaborative",
                        "note": "<i>Initial</i> screening review<!-- secret --><script>alert(1)</script>",
                        "status": "scored",
                        "added": "2026-05-01"
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);

            var applicationsCsv = """
                kind,company,title,req,applied_on,rejected,rejected_date_md,desired_salary,referral,fit,resume_variant,people_detected,review_flags,raw,decision,corrected_notes
                application,Acme Corp,Staff Software Engineer,,2026-05-02,false,,,,,unspecified,,,"<b>Applied</b> via portal<br>Follow up next week",,
                """;
            File.WriteAllText(Path.Combine(tempDir, "applications.csv"), applicationsCsv);

            var inMemoryCtx = new PostingImportStepTests.InMemoryPostingContext();
            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var parser = new Rfc4180CsvParser();
            var postingsStep = new PostingsJsonStep(inMemoryCtx, criteriaStep);
            var appsStep = new ApplicationsStep(parser, inMemoryCtx);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var postResult = await postingsStep.ExecuteAsync(context);
            Assert.True(postResult.IsSuccess);

            var appResult = await appsStep.ExecuteAsync(context);
            Assert.True(appResult.IsSuccess);

            Assert.Single(inMemoryCtx.SavedPostings);
            var posting = inMemoryCtx.SavedPostings[0];

            // Posting title is cleaned of HTML tags
            Assert.Equal("Staff Software Engineer", posting.Title);

            // Culture converted strong -> **, br -> \n
            Assert.Equal("**High** trust\nCollaborative", posting.CultureSummary);

            // Screening note was created with em -> * and script/comment stripped
            Assert.Single(inMemoryCtx.SavedNotes);
            Assert.Equal("*Initial* screening review", inMemoryCtx.SavedNotes[0].Body);

            // Application matched posting by cleaned title!
            Assert.Single(inMemoryCtx.SavedApplications);
            var app = inMemoryCtx.SavedApplications[0];
            Assert.Equal(posting.Id, app.PostingId);

            // Event notes sanitized strong -> ** and br -> \n
            Assert.Single(inMemoryCtx.SavedEvents);
            var evt = inMemoryCtx.SavedEvents[0];
            Assert.Contains("**Applied** via portal\nFollow up next week", evt.Notes);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJsonParent_AppendsToExistingCompanyDescription_WhenUnder300Chars()
    {
        var tempDir = CreateTempDir();
        try
        {
            var existingCompany = Company.Create(
                ownerId: TestOwnerId,
                name: "Acme Corp",
                status: CompanyStatus.Neutral,
                description: "Enterprise cloud software");

            var inMemoryCtx = new PostingImportStepTests.InMemoryPostingContext();
            inMemoryCtx.SavedCompanies.Add(existingCompany);

            var postingsJson = """
                {
                    "role-1": {
                        "company": "Acme Corp",
                        "title": "Principal Engineer",
                        "parent": "Global Parent Industries",
                        "status": "scored",
                        "added": "2026-05-01"
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);

            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var postingsStep = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var postResult = await postingsStep.ExecuteAsync(context);
            Assert.True(postResult.IsSuccess);

            var company = inMemoryCtx.SavedCompanies.First(c => c.Name == "Acme Corp");
            Assert.Equal("Enterprise cloud software; Global Parent Industries", company.Description);

            // Audit entry for company update was appended
            Assert.Contains(inMemoryCtx.AppendedAuditEntries, a =>
                a.Operation == AuditOperation.ImportCompanyUpdate &&
                a.EntityType == "company" &&
                a.EntityId == company.Id);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJsonParent_DropsAndCountsTruncation_WhenExceeds300Chars()
    {
        var tempDir = CreateTempDir();
        try
        {
            var initialDesc = new string('A', 280);
            var existingCompany = Company.Create(
                ownerId: TestOwnerId,
                name: "Acme Corp",
                status: CompanyStatus.Neutral,
                description: initialDesc);

            var inMemoryCtx = new PostingImportStepTests.InMemoryPostingContext();
            inMemoryCtx.SavedCompanies.Add(existingCompany);

            var longParent = new string('B', 50); // 280 + 2 + 50 = 332 > 300
            var postingsJson = $$"""
                {
                    "role-1": {
                        "company": "Acme Corp",
                        "title": "Principal Engineer",
                        "parent": "{{longParent}}",
                        "status": "scored",
                        "added": "2026-05-01"
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);

            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var postingsStep = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var postResult = await postingsStep.ExecuteAsync(context);
            Assert.True(postResult.IsSuccess);

            var company = inMemoryCtx.SavedCompanies.First(c => c.Name == "Acme Corp");
            Assert.Equal(initialDesc, company.Description); // Dropped, description unchanged
            Assert.Equal(1, context.Summary.FieldTruncations["company_description"]);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PostingsJsonParent_Idempotent_DoesNotDuplicateParent_WhenAlreadyContained()
    {
        var tempDir = CreateTempDir();
        try
        {
            var existingCompany = Company.Create(
                ownerId: TestOwnerId,
                name: "Acme Corp",
                status: CompanyStatus.Neutral,
                description: "Enterprise cloud software; Global Parent Industries");

            var inMemoryCtx = new PostingImportStepTests.InMemoryPostingContext();
            inMemoryCtx.SavedCompanies.Add(existingCompany);

            var postingsJson = """
                {
                    "role-1": {
                        "company": "Acme Corp",
                        "title": "Principal Engineer",
                        "parent": "Global Parent Industries",
                        "status": "scored",
                        "added": "2026-05-01"
                    }
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "postings.json"), postingsJson);

            var criteriaStep = new CriteriaSeedingStep(inMemoryCtx);
            var postingsStep = new PostingsJsonStep(inMemoryCtx, criteriaStep);

            var context = new TrackerImportContext
            {
                OwnerId = TestOwnerId,
                BundleDirectory = tempDir,
                DefaultDate = TestDefaultDate
            };

            var postResult = await postingsStep.ExecuteAsync(context);
            Assert.True(postResult.IsSuccess);

            var company = inMemoryCtx.SavedCompanies.First(c => c.Name == "Acme Corp");
            Assert.Equal("Enterprise cloud software; Global Parent Industries", company.Description);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
