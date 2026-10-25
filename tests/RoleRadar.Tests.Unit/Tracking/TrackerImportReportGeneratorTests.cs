using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Commands;
using RoleRadar.Application.Tracking.Import.Report;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public class TrackerImportReportGeneratorTests
{
    [Fact]
    public async Task GenerateReportAsync_IncludesExpectedMappingDecisionsAndUnmappedValuesText()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"report_test_{Guid.NewGuid():N}.txt");
        try
        {
            var command = new ImportTrackerCommand(
                BundleDirectory: "C:\\fake\\bundle",
                DefaultDate: new DateOnly(2026, 10, 24),
                OwnerId: "owner-1",
                Apply: false);

            var summary = new TrackerImportSummary();
            summary.RecordUnmappedValue("remote_mentioned", "not stored");
            summary.RecordUnmappedValue("remote_mentioned", "not stored");
            summary.RecordUnmappedValue("remote_mentioned", "not stored");
            summary.RecordUnmappedValue("fit", "invalid_score");
            summary.ResumeVariantsDefaulted = 5;

            await TrackerImportReportGenerator.GenerateReportAsync(tempFile, command, summary);

            var content = await File.ReadAllTextAsync(tempFile);

            // Assert Mapping Decisions
            Assert.Contains("Mapping Decisions", content);
            Assert.Contains("ResumeVariant.AsIs with 'resume variant unspecified' appended to applied event notes", content);
            Assert.Contains("counted under Unmapped Values as 'remote_mentioned: N not stored'", content);

            // Assert Unmapped Values formatting
            Assert.Contains("Unmapped Values", content);
            Assert.Contains("- remote_mentioned: 3 not stored", content);
            Assert.Contains("- fit: 1 (samples: invalid_score)", content);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
