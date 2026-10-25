using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Pipeline;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class SourceFieldCatalogTests
{
    [Fact]
    public void AppendixRowsCsv_EveryDocumentedHeader_IsPresentInCatalog()
    {
        var catalogFields = SourceFieldCatalog.ByFile["appendix-rows.csv"]
            .Select(f => f.Field)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var header in CsvHeaderValidator.AppendixHeaders)
        {
            Assert.Contains(header, catalogFields);
            var handling = SourceFieldCatalog.GetHandling("appendix-rows.csv", header);
            Assert.False(string.IsNullOrWhiteSpace(handling));
        }
    }

    [Fact]
    public void ApplicationsCsv_EveryDocumentedHeader_IsPresentInCatalog()
    {
        var catalogFields = SourceFieldCatalog.ByFile["applications.csv"]
            .Select(f => f.Field)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var header in CsvHeaderValidator.ApplicationsHeaders)
        {
            Assert.Contains(header, catalogFields);
            var handling = SourceFieldCatalog.GetHandling("applications.csv", header);
            Assert.False(string.IsNullOrWhiteSpace(handling));
        }
    }

    [Fact]
    public void WatchlistCompaniesCsv_EveryDocumentedHeader_IsPresentInCatalog()
    {
        var catalogFields = SourceFieldCatalog.ByFile["watchlist-companies.csv"]
            .Select(f => f.Field)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var header in CsvHeaderValidator.WatchlistHeaders)
        {
            Assert.Contains(header, catalogFields);
            var handling = SourceFieldCatalog.GetHandling("watchlist-companies.csv", header);
            Assert.False(string.IsNullOrWhiteSpace(handling));
        }
    }

    [Fact]
    public void CompanyAliasesCsv_EveryDocumentedHeader_IsPresentInCatalog()
    {
        var catalogFields = SourceFieldCatalog.ByFile["company-aliases.csv"]
            .Select(f => f.Field)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var header in CsvHeaderValidator.CompanyAliasesHeaders)
        {
            Assert.Contains(header, catalogFields);
            var handling = SourceFieldCatalog.GetHandling("company-aliases.csv", header);
            Assert.False(string.IsNullOrWhiteSpace(handling));
        }
    }

    [Fact]
    public void PostingsJson_EveryDocumentedKey_IsPresentInCatalog()
    {
        var expectedKeys = new[]
        {
            "added", "appliedLabel", "comp", "compHigh", "company", "culture", "cultureLevel",
            "first", "ghost", "href", "loc", "note", "order", "parent", "posted", "score",
            "status", "tier", "title"
        };

        var catalogFields = SourceFieldCatalog.ByFile["postings.json"]
            .Select(f => f.Field)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in expectedKeys)
        {
            Assert.Contains(key, catalogFields);
            var handling = SourceFieldCatalog.GetHandling("postings.json", key);
            Assert.False(string.IsNullOrWhiteSpace(handling));
        }
    }

    [Theory]
    [InlineData("appendix-rows.csv", "company_also_in_db")]
    [InlineData("appendix-rows.csv", "review_flags")]
    [InlineData("appendix-rows.csv", "decision")]
    [InlineData("appendix-rows.csv", "corrected_notes")]
    [InlineData("applications.csv", "people_detected")]
    [InlineData("applications.csv", "review_flags")]
    [InlineData("applications.csv", "decision")]
    [InlineData("applications.csv", "corrected_notes")]
    [InlineData("watchlist-companies.csv", "decision")]
    [InlineData("watchlist-companies.csv", "corrected_notes")]
    [InlineData("postings.json", "tier")]
    [InlineData("postings.json", "order")]
    [InlineData("postings.json", "appliedLabel")]
    public void DroppedByDesign_Fields_AreExplicitlyMarked(string file, string field)
    {
        var handling = SourceFieldCatalog.GetHandling(file, field);
        Assert.Equal("dropped by design", handling);
    }

    [Fact]
    public void UnmappedField_ThrowsKeyNotFoundException()
    {
        Assert.Throws<KeyNotFoundException>(() => SourceFieldCatalog.GetHandling("unknown.csv", "nonexistent"));
    }
}
