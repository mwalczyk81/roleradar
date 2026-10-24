using System.Text.Json;
using MongoDB.Driver;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Hosting;

[Collection("ContainerIsolatedCollection")]
public sealed class TrackerImportContainerTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _mongo;

    public TrackerImportContainerTests(IsolatedContainerMongoFixture mongo)
    {
        _mongo = mongo;
    }

    [Fact]
    public async Task Container_ExecutesTrackerImport_WithReadOnlyBundle_AndWritableReportMount()
    {
        await ContainerTestHarness.EnsureImagesBuiltAsync();

        var uniqueDb = $"rrtest_container_import_{Guid.NewGuid():N}";
        var tempBundleDir = ContainerTestHarness.CreateSharedTempDirectory("rr_container_bundle");
        var tempReportDir = ContainerTestHarness.CreateSharedTempDirectory("rr_container_reports");
        var hostReportFile = Path.Combine(tempReportDir, "import-report.txt");
        var containerReportFile = "/reports/import-report.txt";

        try
        {
            // Create minimal valid bundle files
            File.WriteAllText(Path.Combine(tempBundleDir, "watchlist-companies.csv"),
                "company,tier,source,notes,decision,corrected_notes\r\nAcme Corp,watch,recruiter,Note,,\r\n");

            File.WriteAllText(Path.Combine(tempBundleDir, "company-aliases.csv"),
                "alias,canonical\r\nAcme,Acme Corp\r\n");

            File.WriteAllText(Path.Combine(tempBundleDir, "postings.json"),
                """
                {
                  "acme-container-eng": {
                    "company": "Acme Corp",
                    "title": "Container Systems Engineer",
                    "href": "https://example.com/job/container1",
                    "score": 8.5
                  }
                }
                """);

            File.WriteAllText(Path.Combine(tempBundleDir, "appendix-rows.csv"),
                "req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes\r\n");

            File.WriteAllText(Path.Combine(tempBundleDir, "applications.csv"),
                "req_id,company,role,applied_date_md,fit,interview_lead,notes,raw,outcome,status,rejected_date_md,reason_rejected,rejected_stage,contains_people,decision,corrected_notes\r\n");

            // Snapshot bundle file hashes/times
            var initialPostingsJson = await File.ReadAllTextAsync(Path.Combine(tempBundleDir, "postings.json"));
            var initialWatchlistCsv = await File.ReadAllTextAsync(Path.Combine(tempBundleDir, "watchlist-companies.csv"));

            var env = new Dictionary<string, string>
            {
                ["ROLERADAR_MONGO_CONNECTION_STRING"] = _mongo.ContainerMongoUri,
                ["ROLERADAR_MONGO_DATABASE"] = uniqueDb
            };

            var volumes = new[]
            {
                $"{tempBundleDir}:/bundle:ro",
                $"{tempReportDir}:/reports:rw"
            };

            // Run tracker-import inside container in dry-run mode with --report
            var execResult = await ContainerTestHarness.RunCliAsync(
                [
                    "tracker-import",
                    "/bundle",
                    "--default-date", "2026-10-24",
                    "--report", containerReportFile
                ],
                environment: env,
                volumes: volumes,
                network: _mongo.NetworkName,
                timeoutSeconds: 60);

            Assert.True(execResult.ExitCode == 0, $"Container tracker-import failed with exit {execResult.ExitCode}.\nSTDOUT: {execResult.StandardOutput}\nSTDERR: {execResult.StandardError}");

            // Assert external report was generated and is valid UTF-8
            Assert.True(File.Exists(hostReportFile), "Report file was not created in the mounted writable directory.");
            var reportContent = await File.ReadAllTextAsync(hostReportFile);
            Assert.Contains("RoleRadar Tracker Import Execution Report", reportContent);
            Assert.Contains("DRY_RUN", reportContent);

            // Assert bundle files were not modified (read-only enforcement)
            var currentPostingsJson = await File.ReadAllTextAsync(Path.Combine(tempBundleDir, "postings.json"));
            var currentWatchlistCsv = await File.ReadAllTextAsync(Path.Combine(tempBundleDir, "watchlist-companies.csv"));
            Assert.Equal(initialPostingsJson, currentPostingsJson);
            Assert.Equal(initialWatchlistCsv, currentWatchlistCsv);

            var client = new MongoClient(_mongo.HostMongoUri);
            await client.DropDatabaseAsync(uniqueDb);
        }
        finally
        {
            try { Directory.Delete(tempBundleDir, true); } catch { }
            try { Directory.Delete(tempReportDir, true); } catch { }
        }
    }
}
