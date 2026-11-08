using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using RoleRadar.Tests.Unit.Support;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

/// <summary>
/// Properties of import normalization and dedupe (feature 011, story 3). Normalizers must be idempotent and blind to
/// tracking noise; posting dedupe must assign the same tiers to the same rows whatever order they arrive in. Data is
/// fictional and drawn from small pools so collisions happen often. Reuses the in-memory context of the posting import
/// tests instead of duplicating fakes.
/// </summary>
public sealed class ImportNormalizationPropertyTests
{
    private const string Owner = "owner-fictional-property";

    private static readonly string[] Hosts = ["jobs.example.com", "Careers.Example.ORG", "boards.example.net"];
    private static readonly string[] Paths = ["", "/", "/jobs/101", "/jobs/101/", "/Jobs/Role-7//", "/a%20b"];
    private static readonly string[] KeepParams = ["req_id=999", "team=core", "lang=en"];
    private static readonly string[] TrackingParams = ["utm_source=x", "ref=feed", "gh_src=abc", "fbclid=1", "ref_code=2", "source_id=3"];
    private static readonly string[] TitleTokens = ["Senior", "STAFF", "Engineer", "(.NET)", "-", "  ", "Platform", "#1", "élève", "日本", "!!!", "/"];

    private static string Pick(string[] pool, int code) => pool[Math.Abs(code % pool.Length)];

    private static string BuildUrl(int[] codes, bool withTracking, bool upperScheme, bool explicitDefaultPort)
    {
        var scheme = codes.Length > 0 && codes[0] % 2 == 0 ? "https" : "http";
        var host = Pick(Hosts, codes.Length > 1 ? codes[1] : 0);
        var path = Pick(Paths, codes.Length > 2 ? codes[2] : 0);
        var keep = codes.Skip(3).Take(2).Select(c => Pick(KeepParams, c)).Distinct().ToList();
        var tracking = codes.Skip(5).Take(3).Select(c => Pick(TrackingParams, c)).ToList();

        var query = new List<string>(keep);
        if (withTracking)
        {
            query.InsertRange(query.Count > 0 ? 1 : 0, tracking);
        }

        var port = explicitDefaultPort ? (scheme == "https" ? ":443" : ":80") : string.Empty;
        var shownScheme = upperScheme ? scheme.ToUpperInvariant() : scheme;
        return $"{shownScheme}://{host}{port}{path}{(query.Count > 0 ? "?" + string.Join("&", query) : string.Empty)}";
    }

    // ---- normalization ----

    [Fact]
    public void Property_UrlNormalizer_IsIdempotent_OverGeneratedUrls()
    {
        PropertyRunner.Check(FsCheck.Fluent.Prop.ForAll(PropertyRunner.Codes(11), codes =>
        {
            var url = BuildUrl(codes, withTracking: true, upperScheme: false, explicitDefaultPort: false);
            var once = UrlNormalizer.Normalize(url);
            return string.Equals(once, UrlNormalizer.Normalize(once), StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void Property_UrlNormalizer_IgnoresTrackingParams_SchemeCase_AndExplicitDefaultPort()
    {
        PropertyRunner.Check(FsCheck.Fluent.Prop.ForAll(PropertyRunner.Codes(11), codes =>
        {
            var clean = BuildUrl(codes, withTracking: false, upperScheme: false, explicitDefaultPort: false);
            var noisy = BuildUrl(codes, withTracking: true, upperScheme: true, explicitDefaultPort: true);
            return string.Equals(UrlNormalizer.Normalize(clean), UrlNormalizer.Normalize(noisy), StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void Property_TitleNormalizer_IsIdempotent_AndLowercaseWithSingleSpaces()
    {
        PropertyRunner.Check(FsCheck.Fluent.Prop.ForAll(PropertyRunner.Codes(11), codes =>
        {
            var title = string.Join(' ', codes.Take(12).Select(c => Pick(TitleTokens, c)));
            var once = TitleNormalizer.Normalize(title);
            var twice = TitleNormalizer.Normalize(once);
            return string.Equals(once, twice, StringComparison.Ordinal)
                && !once.Contains("  ", StringComparison.Ordinal)
                && once == once.Trim()
                && !once.Any(char.IsUpper);
        }));
    }

    // ---- dedupe ----

    private sealed class FixedClock : IUtcClock
    {
        public DateTime UtcNow { get; } = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
    }

    private sealed record IncomingRow(string Title, string? Tenant, string? Requisition, string? Url);

    private static readonly string?[] Tenants = [null, "tenant-a", "tenant-b"];
    private static readonly string?[] Requisitions = [null, "101", "102"];
    private static readonly string?[] Urls =
    [
        null,
        "https://jobs.example.com/p/1",
        "https://jobs.example.com/p/1?utm_source=feed",
        "https://jobs.example.com/p/2/",
    ];

    /// <summary>
    /// Rows have unique titles so the advisory title tier cannot interfere, and each row carries exactly one kind of
    /// identity (tenant and requisition, URL only, or requisition only), so no row bridges two existing postings.
    /// Bridging rows are order dependent today and are pinned separately, see <see cref="PinsTodaysOrderDependentBehavior_ABridgingRowJoinsWhicheverPostingIsLookedUpFirst"/>.
    /// </summary>
    private static List<IncomingRow> BuildRows(int[] codes)
    {
        var rows = new List<IncomingRow>();
        foreach (var chunk in codes.Take(24).Chunk(3))
        {
            var kind = Math.Abs(chunk[0] % 3);
            var pick = Math.Abs((chunk.Length > 1 ? chunk[1] : 0) % 2);
            var title = $"Role {rows.Count + 1}";
            rows.Add(kind switch
            {
                0 => new IncomingRow(title, Tenants[1 + pick], Requisitions[1 + Math.Abs((chunk.Length > 2 ? chunk[2] : 0) % 2)], null),
                1 => new IncomingRow(title, null, null, Urls[1 + Math.Abs((chunk.Length > 2 ? chunk[2] : 0) % 3)]),
                _ => new IncomingRow(title, null, Requisitions[1 + pick], null),
            });
        }

        return rows;
    }

    /// <summary>Ingests the rows in the given order and returns, per original row index, the id of the posting it landed on.</summary>
    private static async Task<(Guid[] Landed, int StoredPostings)> IngestAsync(IReadOnlyList<IncomingRow> rows, IReadOnlyList<int> order)
    {
        var context = new PostingImportStepTests.InMemoryPostingContext();
        var company = Company.Create(Owner, "Acme Fictional");
        context.SavedCompanies.Add(company);
        var handler = new RecordPostingCommandHandler(context, new FixedClock());

        var landed = new Guid[rows.Count];
        foreach (var index in order)
        {
            var row = rows[index];
            var result = await handler.HandleAsync(new RecordPostingCommand(
                CompanyId: company.Id,
                Title: row.Title,
                Url: row.Url,
                AtsTenant: row.Tenant,
                RequisitionId: row.Requisition,
                OwnerId: Owner));

            Assert.True(result.IsSuccess, "Ingest must succeed for generated rows.");
            landed[index] = result.Value!.Posting?.Id ?? Guid.Empty;
        }

        return (landed, context.SavedPostings.Count);
    }

    /// <summary>The grouping of rows into postings, independent of the generated ids.</summary>
    private static string Partition(Guid[] landed)
        => string.Join("|", landed
            .Select((id, index) => (id, index))
            .GroupBy(x => x.id)
            .Select(g => string.Join(",", g.Select(x => x.index)))
            .OrderBy(s => s, StringComparer.Ordinal));

    [Fact]
    public void Property_DedupeTiers_GroupRowsIdentically_WhateverOrderTheyArrive()
    {
        PropertyRunner.Check(FsCheck.Fluent.Prop.ForAll(PropertyRunner.Codes(11), PropertyRunner.Codes(31), (rowCodes, shuffleCodes) =>
        {
            var rows = BuildRows(rowCodes);
            if (rows.Count == 0)
            {
                return true;
            }

            var inOrder = Enumerable.Range(0, rows.Count).ToList();
            var shuffled = inOrder
                .OrderBy(i => shuffleCodes.Length == 0 ? i : shuffleCodes[i % shuffleCodes.Length] * 31 + i)
                .ToList();

            var first = Partition(IngestAsync(rows, inOrder).GetAwaiter().GetResult().Landed);
            var second = Partition(IngestAsync(rows, shuffled).GetAwaiter().GetResult().Landed);
            return string.Equals(first, second, StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void Property_DedupeTiers_ReListingTheSameRowsUnderANewTitleCreatesNothingNew()
    {
        PropertyRunner.Check(FsCheck.Fluent.Prop.ForAll(PropertyRunner.Codes(11), rowCodes =>
        {
            var rows = BuildRows(rowCodes);
            if (rows.Count == 0)
            {
                return true;
            }

            // The second pass re-lists every row under a new title, so only the A, B and C tiers can recognize it.
            var relisted = rows.Select(r => r with { Title = r.Title + " relisted" });
            var both = rows.Concat(relisted).ToList();
            var doubled = IngestAsync(both, Enumerable.Range(0, both.Count).ToList()).GetAwaiter().GetResult();
            var once = IngestAsync(rows, Enumerable.Range(0, rows.Count).ToList()).GetAwaiter().GetResult();
            return doubled.StoredPostings == once.StoredPostings;
        }));
    }

    [Fact]
    public async Task PinsTodaysOrderDependentBehavior_ABridgingRowJoinsWhicheverPostingIsLookedUpFirst()
    {
        // Found by Property_DedupeTiers_GroupRowsIdentically_WhateverOrderTheyArrive (shrunk counterexample). Row 0 has a
        // requisition only, row 1 has a URL only, row 2 has both. The URL tier is checked before the requisition tier,
        // a matched posting never gains the new URL, and postings are never merged. Row 2 therefore joins row 1 when row 1
        // is stored first, and joins row 0 (by requisition) otherwise, which then leaves row 1 as a separate posting.
        // This test PINS TODAY'S ORDER-DEPENDENT BEHAVIOR; it does not endorse it. Tracked as "board: Import dedupe depends on
        // row order" (research.md D11). When that card is resolved by a design decision, change the expected groupings here
        // so that both orders give the same grouping, and the property above can then include bridging rows.
        var rows = new List<IncomingRow>
        {
            new("Role 1", null, "101", null),
            new("Role 2", null, null, "https://jobs.example.com/p/1"),
            new("Role 3", null, "101", "https://jobs.example.com/p/1"),
        };

        var urlRowFirst = Partition((await IngestAsync(rows, [0, 1, 2])).Landed);
        var bridgingRowFirst = Partition((await IngestAsync(rows, [0, 2, 1])).Landed);

        Assert.Equal("0|1,2", urlRowFirst);
        Assert.Equal("0,2|1", bridgingRowFirst);
    }
}
