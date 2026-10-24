using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Commands;
using RoleRadar.Cli.Commands;
using RoleRadar.Infrastructure;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Import;

[Collection("ContainerIsolatedCollection")]
public sealed class TrackerImportReportingTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _fixture;

    public TrackerImportReportingTests(IsolatedContainerMongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ConsoleOutput_ContainsZeroSentinelStrings_AndOnlyErrorCodesAndNumbers()
    {
        var tempBundleDir = Path.Combine(Path.GetTempPath(), $"rr_reporting_bundle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBundleDir);

        const string sentinelCompany = "SentinelCorpOmega";
        const string sentinelRole = "Senior Sentinel Architect";
        const string sentinelNote = "Sentinel Secret Note Content";
        const string sentinelCulture = "Sentinel Culture Details High Risk";
        const string sentinelReason = "SentinelReasonInternalRejection";

        try
        {
            File.WriteAllText(Path.Combine(tempBundleDir, "watchlist-companies.csv"),
                $"company,tier,source,notes,decision,corrected_notes\r\n{sentinelCompany},watch,recruiter,{sentinelCulture},,\r\nBadCompany,watch,recruiter,notes,invalid_decision,\r\n");

            File.WriteAllText(Path.Combine(tempBundleDir, "company-aliases.csv"),
                $"alias,canonical\r\nSentinelAlias,{sentinelCompany}\r\n");

            File.WriteAllText(Path.Combine(tempBundleDir, "postings.json"),
                $$"""
                {
                  "sentinel-entry": {
                    "company": "{{sentinelCompany}}",
                    "title": "{{sentinelRole}}",
                    "href": "https://example.com/job/sentinel1",
                    "score": 14.5,
                    "culture": "{{sentinelCulture}}"
                  }
                }
                """);

            File.WriteAllText(Path.Combine(tempBundleDir, "appendix-rows.csv"),
                $"req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes\r\n101,{sentinelCompany},{sentinelRole},true,Details,2026-10-15,{sentinelReason},{sentinelReason},never,false,https://example.com/job/sentinel1,,\r\n");

            File.WriteAllText(Path.Combine(tempBundleDir, "applications.csv"),
                $"req_id,company,role,applied_date_md,fit,interview_lead,notes,raw,outcome,status,rejected_date_md,reason_rejected,rejected_stage,contains_people,decision,corrected_notes\r\n101,{sentinelCompany},{sentinelRole},2026-10-15,good,lead,{sentinelNote},raw,rejected,applied,2026-10-20,comp,stage,contains_people,,\r\n");

            var dbName = $"rrtest_report_{Guid.NewGuid():N}";
            var services = BuildServiceProvider(dbName);
            var command = TrackerImportCommand.Create(services);

            var swOut = new StringWriter();
            var swErr = new StringWriter();
            var origOut = Console.Out;
            var origErr = Console.Error;

            int exitCode;
            try
            {
                Console.SetOut(swOut);
                Console.SetError(swErr);

                var parseResult = command.Parse($"\"{tempBundleDir}\" --default-date 2026-10-24");
                exitCode = await parseResult.InvokeAsync();
            }
            finally
            {
                Console.SetOut(origOut);
                Console.SetError(origErr);
            }

            var stdout = swOut.ToString();
            var stderr = swErr.ToString();

            // Assert zero sentinel strings in stdout or stderr
            Assert.DoesNotContain(sentinelCompany, stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sentinelCompany, stderr, StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(sentinelRole, stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sentinelRole, stderr, StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(sentinelNote, stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sentinelNote, stderr, StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(sentinelCulture, stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sentinelCulture, stderr, StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(sentinelReason, stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sentinelReason, stderr, StringComparison.OrdinalIgnoreCase);

            // Assert error code is displayed with file and row number without error message
            Assert.Contains("watchlist-companies.csv:3", stdout);
            Assert.Contains("ValidationError", stdout);
            Assert.DoesNotContain("Invalid decision value", stdout, StringComparison.OrdinalIgnoreCase);

            var client = new MongoClient(_fixture.HostMongoUri);
            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempBundleDir, true);
        }
    }

    [Fact]
    public async Task PostingsJson_RowErrors_HaveOneBasedOrdinal_NeverZero()
    {
        var tempBundleDir = Path.Combine(Path.GetTempPath(), $"rr_reporting_ordinal_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBundleDir);

        try
        {
            File.WriteAllText(Path.Combine(tempBundleDir, "watchlist-companies.csv"), "company,tier,source,notes,decision,corrected_notes\r\n");
            File.WriteAllText(Path.Combine(tempBundleDir, "company-aliases.csv"), "alias,canonical\r\n");
            File.WriteAllText(Path.Combine(tempBundleDir, "appendix-rows.csv"), "req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes\r\n");
            File.WriteAllText(Path.Combine(tempBundleDir, "applications.csv"), "req_id,company,role,applied_date_md,fit,interview_lead,notes,raw,outcome,status,rejected_date_md,reason_rejected,rejected_stage,contains_people,decision,corrected_notes\r\n");

            // Element 1 is valid, element 2 is missing company and title (row error)
            File.WriteAllText(Path.Combine(tempBundleDir, "postings.json"),
                """
                {
                  "valid-slug-1": {
                    "company": "Valid Company",
                    "title": "Valid Role",
                    "href": "https://example.com/job/valid1"
                  },
                  "invalid-slug-2": {
                    "company": "",
                    "title": ""
                  }
                }
                """);

            var dbName = $"rrtest_ordinal_{Guid.NewGuid():N}";
            var services = BuildServiceProvider(dbName);
            var coordinator = services.GetRequiredService<ITrackerImportCoordinator>();

            var importCmd = new ImportTrackerCommand(
                BundleDirectory: tempBundleDir,
                DefaultDate: new DateOnly(2026, 10, 24),
                Apply: false);

            var result = await coordinator.ExecuteAsync(importCmd);

            Assert.True(result.IsSuccess);
            var summary = result.Value!;
            var postingError = Assert.Single(summary.Errors, e => e.SourceFile == "postings.json");
            Assert.Equal(2, postingError.LineNumber); // 1-based ordinal
            Assert.NotEqual(0, postingError.LineNumber);

            var client = new MongoClient(_fixture.HostMongoUri);
            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempBundleDir, true);
        }
    }

    [Fact]
    public async Task ReportOption_InsideGitWorkTree_FailsWithExitCode1_AndWritesNoFile()
    {
        var tempBundleDir = Path.Combine(Path.GetTempPath(), $"rr_gitworktree_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBundleDir);

        // Path inside current directory (git repo)
        var insideGitReportPath = Path.Combine(Directory.GetCurrentDirectory(), $"test_forbidden_report_{Guid.NewGuid():N}.txt");

        try
        {
            File.WriteAllText(Path.Combine(tempBundleDir, "watchlist-companies.csv"), "company,tier,source,notes,decision,corrected_notes\r\n");
            File.WriteAllText(Path.Combine(tempBundleDir, "company-aliases.csv"), "alias,canonical\r\n");
            File.WriteAllText(Path.Combine(tempBundleDir, "postings.json"), "{}");
            File.WriteAllText(Path.Combine(tempBundleDir, "appendix-rows.csv"), "req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes\r\n");
            File.WriteAllText(Path.Combine(tempBundleDir, "applications.csv"), "req_id,company,role,applied_date_md,fit,interview_lead,notes,raw,outcome,status,rejected_date_md,reason_rejected,rejected_stage,contains_people,decision,corrected_notes\r\n");

            var dbName = $"rrtest_gitrefuse_{Guid.NewGuid():N}";
            var services = BuildServiceProvider(dbName);
            var command = TrackerImportCommand.Create(services);

            var parseResult = command.Parse($"\"{tempBundleDir}\" --default-date 2026-10-24 --report \"{insideGitReportPath}\"");
            var exitCode = await parseResult.InvokeAsync();

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(insideGitReportPath));

            var client = new MongoClient(_fixture.HostMongoUri);
            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempBundleDir, true);
            if (File.Exists(insideGitReportPath))
            {
                File.Delete(insideGitReportPath);
            }
        }
    }

    [Fact]
    public async Task ReportOption_OutsideGitWorkTree_WritesUtf8Report_WithAllRequiredSections()
    {
        var tempBundleDir = Path.Combine(Path.GetTempPath(), $"rr_reporting_ext_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBundleDir);

        var externalReportDir = Path.Combine(Path.GetTempPath(), $"rr_report_out_{Guid.NewGuid():N}");
        Directory.CreateDirectory(externalReportDir);
        var reportPath = Path.Combine(externalReportDir, "import-report.txt");

        try
        {
            File.WriteAllText(Path.Combine(tempBundleDir, "watchlist-companies.csv"), "company,tier,source,notes,decision,corrected_notes\r\n");
            File.WriteAllText(Path.Combine(tempBundleDir, "company-aliases.csv"), "alias,canonical\r\n");

            // Postings has an auto-created neutral company, and an unclaimed applied posting (object keyed by slug)
            File.WriteAllText(Path.Combine(tempBundleDir, "postings.json"),
                """
                {
                  "unlisted-tech-principal-arch": {
                    "company": "Unlisted Tech Corp",
                    "title": "Principal Architect",
                    "status": "applied",
                    "href": "https://example.com/job/unlisted1"
                  }
                }
                """);

            File.WriteAllText(Path.Combine(tempBundleDir, "appendix-rows.csv"), "req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes\r\n");

            // Applications has a detected person
            File.WriteAllText(Path.Combine(tempBundleDir, "applications.csv"),
                "req_id,company,role,applied_date_md,fit,interview_lead,notes,raw,outcome,status,rejected_date_md,reason_rejected,rejected_stage,review_flags,people_detected,decision,corrected_notes\r\n" +
                "201,Other Corp,Dev,2026-10-15,good,lead,notes,raw,active,applied,,,,contains_people,Jane Doe,,\r\n");

            var dbName = $"rrtest_reportext_{Guid.NewGuid():N}";
            var services = BuildServiceProvider(dbName);
            var command = TrackerImportCommand.Create(services);

            var parseResult = command.Parse($"\"{tempBundleDir}\" --default-date 2026-10-24 --report \"{reportPath}\"");
            var exitCode = await parseResult.InvokeAsync();

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(reportPath));

            var reportContent = await File.ReadAllTextAsync(reportPath);

            // Section 1: Header & Execution Mode
            Assert.Contains("RoleRadar Tracker Import Execution Report", reportContent);
            Assert.Contains("DRY_RUN", reportContent);
            Assert.Contains("2026-10-24", reportContent);

            // Section 2: Aggregated Counts Summary
            Assert.Contains("Aggregated Counts Summary", reportContent);
            Assert.Contains("Companies Created", reportContent);
            Assert.Contains("Postings Created", reportContent);

            // Section 3: Companies Requiring Alias Review
            Assert.Contains("Companies Requiring Alias Review", reportContent);
            Assert.Contains("Unlisted Tech Corp", reportContent);

            // Section 4: Unclaimed "Applied" Postings
            Assert.Contains("Unclaimed Applied Postings", reportContent);
            Assert.Contains("Principal Architect", reportContent);

            // Section 5: Contacts Detected for Manual Entry
            Assert.Contains("Contacts Detected for Manual Entry", reportContent);
            Assert.Contains("roleradar contact add", reportContent);

            // File-Level Reconciliation Summary Section
            Assert.Contains("File-Level Reconciliation Summary", reportContent);
            Assert.Contains("postings.json", reportContent);
            Assert.Contains("applications.csv", reportContent);

            var client = new MongoClient(_fixture.HostMongoUri);
            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempBundleDir, true);
            Directory.Delete(externalReportDir, true);
        }
    }

    private ServiceProvider BuildServiceProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        RoleRadar.Infrastructure.DependencyInjection.AddRoleRadarInfrastructure(services, opts =>
        {
            opts.ConnectionString = _fixture.HostMongoUri;
            opts.DatabaseName = dbName;
        });
        services.AddRoleRadarApplication();

        return services.BuildServiceProvider();
    }
}
