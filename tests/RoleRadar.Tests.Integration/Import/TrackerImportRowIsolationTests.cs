using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Cli.Commands;
using RoleRadar.Domain.Tracking;
using RoleRadar.Infrastructure;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Import;

[Collection("ContainerIsolatedCollection")]
public sealed class TrackerImportRowIsolationTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _fixture;

    public TrackerImportRowIsolationTests(IsolatedContainerMongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PerRowIsolation_OneInvalidRowPerFile_ContinuesImport_ExitCodeReflectsErrors_AndConsoleRestricted()
    {
        var tempBundleDir = Path.Combine(Path.GetTempPath(), $"rr_row_iso_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBundleDir);

        const string validCompany = "FictionalIsoCorp";
        const string validTitle1 = "Platform Engineer";
        const string validTitle2 = "Site Reliability Lead";

        try
        {
            // 1. watchlist-companies.csv: Row 2 is valid; Row 3 has bad enum (decision: invalid_choice)
            await File.WriteAllTextAsync(Path.Combine(tempBundleDir, "watchlist-companies.csv"),
                $"company,tier,source,notes,decision,corrected_notes\r\n" +
                $"{validCompany},watch,referral,Valid company description,,\r\n" +
                $"BadEnumCompany,watch,referral,Notes,invalid_decision_choice,\r\n");

            // 2. postings.json: Entry 1 is valid; Entry 2 has missing required column (missing company)
            await File.WriteAllTextAsync(Path.Combine(tempBundleDir, "postings.json"),
                $$"""
                {
                  "iso-valid-1": {
                    "company": "{{validCompany}}",
                    "title": "{{validTitle1}}",
                    "href": "https://example.com/job/iso-valid1",
                    "score": 8.0,
                    "culture": "Moderate risk"
                  },
                  "iso-invalid-2": {
                    "title": "Missing Company Role",
                    "href": "https://example.com/job/missing-company"
                  }
                }
                """);

            // 3. appendix-rows.csv: Row 2 is valid; Row 3 has malformed date
            await File.WriteAllTextAsync(Path.Combine(tempBundleDir, "appendix-rows.csv"),
                $"req_id,company,title,remote_mentioned,details_raw,added_md,reason_primary,reason_all,proposed_resurface,company_also_in_db,url,decision,corrected_notes\r\n" +
                $"ISO-101,{validCompany},{validTitle2},true,Raw,2026-10-15,comp_below_floor,comp_below_floor,never,false,https://example.com/job/iso-valid2,,\r\n" +
                $"ISO-102,{validCompany},Malformed Date Role,false,Raw,malformed-date-value,comp_below_floor,comp_below_floor,never,false,,,\r\n");

            // 4. applications.csv: Row 2 is valid; Row 3 has invalid domain argument (negative desired salary)
            await File.WriteAllTextAsync(Path.Combine(tempBundleDir, "applications.csv"),
                $"req_id,company,role,applied_date_md,desired_salary,resume_variant,notes,raw,outcome,status,rejected_date_md,reason_rejected,rejected_stage,contains_people,decision,corrected_notes\r\n" +
                $"ISO-101,{validCompany},{validTitle2},2026-10-15,210000,tailored,Applied note,raw,applied,applied,,,,,,,\r\n" +
                $"ISO-103,{validCompany},Negative Salary Role,2026-10-15,-50000,tailored,Note,raw,applied,applied,,,,,,,\r\n");

            var dbName = $"rrtest_iso_{Guid.NewGuid():N}";
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

                // Run with --apply to verify valid rows persist while invalid rows roll back
                var parseResult = command.Parse($"\"{tempBundleDir}\" --default-date 2026-10-24 --apply");
                exitCode = await parseResult.InvokeAsync();
            }
            finally
            {
                Console.SetOut(origOut);
                Console.SetError(origErr);
            }

            var stdout = swOut.ToString();
            var stderr = swErr.ToString();

            // 1. Assert exit code reflects row errors (non-zero)
            Assert.NotEqual(0, exitCode);

            // 2. Assert console shows ONLY [file:row] ErrorCode without leaking entity or message text
            Assert.Contains("[watchlist-companies.csv:3] ValidationError", stdout);
            Assert.Contains("[postings.json:2] ValidationError", stdout);
            Assert.Contains("[appendix-rows.csv:3] ValidationError", stdout);
            Assert.Contains("[applications.csv:3] ValidationError", stdout);

            Assert.DoesNotContain("invalid_decision_choice", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Missing required title or company", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("malformed-date-value", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Desired salary minimum must be >= 0", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Negative Salary Role", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Malformed Date Role", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Missing Company Role", stdout, StringComparison.OrdinalIgnoreCase);

            // 3. Assert all valid rows from every file imported into database
            var client = new MongoClient(_fixture.HostMongoUri);
            var db = client.GetDatabase(dbName);

            var compCol = db.GetCollection<BsonDocument>("companies");
            var postCol = db.GetCollection<BsonDocument>("postings");
            var appCol = db.GetCollection<BsonDocument>("applications");

            var companies = await compCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
            var postings = await postCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();
            var applications = await appCol.Find(Builders<BsonDocument>.Filter.Empty).ToListAsync();

            // Valid company imported
            Assert.Contains(companies, c => c["name"].AsString == validCompany);
            // Bad company did NOT import
            Assert.DoesNotContain(companies, c => c["name"].AsString == "BadEnumCompany");

            // Valid postings imported
            Assert.Contains(postings, p => p["title"].AsString == validTitle1);
            Assert.Contains(postings, p => p["title"].AsString == validTitle2);
            // Invalid postings did NOT import
            Assert.DoesNotContain(postings, p => p["title"].AsString == "Missing Company Role");
            Assert.DoesNotContain(postings, p => p["title"].AsString == "Malformed Date Role");
            Assert.DoesNotContain(postings, p => p["title"].AsString == "Negative Salary Role");

            // Valid application imported
            Assert.Single(applications);

            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempBundleDir, true);
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
