namespace RoleRadar.Tests.Integration;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

/// <summary>
/// Phase 6 (T062, SC-007): filtered, sorted and paged queries over 5,000 documents per entity set return in under
/// one second. Each query is warmed up first, then timed several times; the assertion is on the median (the max
/// is reported with every result). The threshold is the spec's one second and is not relaxed.
/// </summary>
public sealed class ODataBenchmarkTests : IClassFixture<BenchmarkFixture>
{
    private const int Warmups = 3;
    private const int Samples = 9;
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(1);

    private readonly BenchmarkFixture _fixture;

    public ODataBenchmarkTests(BenchmarkFixture fixture) => _fixture = fixture;

    public static TheoryData<string, string, string> Queries => new()
    {
        // Postings: default list, case-insensitive text sort, nulls-last score sort both ways, filter + sort + deep page.
        { "Postings", "default list", "" },
        { "Postings", "title asc (collation), page 2", "$orderby=title asc&$top=25&$skip=25" },
        { "Postings", "score asc nulls last", "$orderby=score asc&$top=25" },
        { "Postings", "score desc nulls last, deep page", "$orderby=score desc&$top=25&$skip=4000" },
        { "Postings", "filter disposition + sort + count", "$filter=disposition eq 'lead'&$orderby=updatedAt desc&$top=25&$count=true" },
        { "Postings", "lowercase title search + sort", "$filter=contains(tolower(title),'engineer 4')&$orderby=title asc&$top=25&$count=true" },
        { "Postings", "select projection", "$select=title,companyName&$orderby=companyName asc&$top=50" },

        // Companies
        { "Companies", "name asc, deep page", "$orderby=name asc&$top=25&$skip=2500" },
        { "Companies", "filter status + name desc + count", "$filter=status eq 'active'&$orderby=name desc&$top=25&$count=true" },
        { "Companies", "name search", "$filter=contains(tolower(name),'company 12')&$top=25&$count=true" },
        { "Companies", "ownershipType sort", "$orderby=ownershipType asc&$top=25&$skip=1000" },

        // Applications
        { "Applications", "firstResponseAt asc nulls last", "$orderby=firstResponseAt asc&$top=25" },
        { "Applications", "firstResponseAt desc nulls last", "$orderby=firstResponseAt desc&$top=25&$skip=2000" },
        { "Applications", "lastEventAt desc nulls last", "$orderby=lastEventAt desc&$top=25" },
        { "Applications", "filter status + appliedAt sort + count", "$filter=status eq 'applied'&$orderby=appliedAt desc&$top=25&$skip=1000&$count=true" },
        { "Applications", "company search", "$filter=contains(tolower(companyName),'company 7')&$orderby=companyName asc&$top=25" },

        // Notes: the default list never reads content; sorts and heading search stay on indexes.
        { "Notes", "notes list (default, no content)", "" },
        { "Notes", "notes list deep page", "$top=25&$skip=4000&$count=true" },
        { "Notes", "heading asc (collation)", "$orderby=heading asc&$top=25&$skip=100" },
        { "Notes", "heading search", "$filter=contains(tolower(heading),'note 33')&$orderby=updatedAt desc&$top=25&$count=true" },
        { "Notes", "created filter + sort", "$filter=createdAt ge 2026-01-01T00:00:00Z&$orderby=createdAt desc&$top=25&$count=true" },

        // Criteria
        { "Criteria", "effectiveTo asc nulls last", "$orderby=effectiveTo asc&$top=25" },
        { "Criteria", "effectiveTo desc nulls last", "$orderby=effectiveTo desc&$top=25&$skip=2000" },
        { "Criteria", "name asc (collation)", "$orderby=name asc&$top=25&$skip=500" },
        { "Criteria", "interval filter + sort + count", "$filter=(effectiveFrom le 2026-06-01T00:00:00Z) and ((effectiveTo eq null) or (effectiveTo gt 2026-06-01T00:00:00Z))&$orderby=name asc&$top=25&$count=true" },
        { "Criteria", "category filter", "$filter=category eq 'weight' and isActive eq true&$orderby=effectiveFrom desc&$top=25&$count=true" },

        // SearchRuns
        { "SearchRuns", "default (runDate desc)", "" },
        { "SearchRuns", "runDate asc, deep page", "$orderby=runDate asc&$top=25&$skip=4000" },
        { "SearchRuns", "newPostings filter + sort + count", "$filter=newPostings ge 5&$orderby=newPostings desc&$top=25&$count=true" }
    };

    [Fact]
    public async Task Every_Entity_Set_Holds_5000_Documents_For_The_Caller()
    {
        foreach (var set in new[] { "Postings", "Companies", "Applications", "Notes", "Criteria", "SearchRuns" })
        {
            var (status, body) = await _fixture.Host.GetJsonAsync(BenchmarkFixture.Owner, $"/odata/{set}?$count=true&$top=1");
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(BenchmarkFixture.DocumentsPerSet, body.RootElement.GetProperty("@odata.count").GetInt32());
        }
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public async Task Query_Completes_In_Under_One_Second(string set, string label, string query)
    {
        var path = $"/odata/{set}" + (query.Length == 0 ? string.Empty : "?" + query);

        for (var i = 0; i < Warmups; i++)
        {
            await TimeOnceAsync(path);
        }

        var samples = new List<double>();
        for (var i = 0; i < Samples; i++)
        {
            samples.Add(await TimeOnceAsync(path));
        }

        samples.Sort();
        var median = samples[samples.Count / 2];
        var max = samples[^1];
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"BENCH {set,-12} | {label,-42} | median {median,7:F1} ms | max {max,7:F1} ms | n={Samples} | {path}");
        TestContext.Current.TestOutputHelper?.WriteLine(line);

        Assert.True(
            median < Threshold.TotalMilliseconds,
            $"{set} '{label}' median {median:F1} ms (max {max:F1} ms) is not under {Threshold.TotalMilliseconds} ms: {path}");
    }

    private async Task<double> TimeOnceAsync(string path)
    {
        var stopwatch = Stopwatch.StartNew();
        using var response = await _fixture.Host.GetAsync(BenchmarkFixture.Owner, path);
        var raw = await response.Content.ReadAsStringAsync();
        stopwatch.Stop();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{path} returned {(int)response.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        Assert.True(doc.RootElement.GetProperty("value").GetArrayLength() > 0, $"a benchmark query must return rows: {path}");
        return stopwatch.Elapsed.TotalMilliseconds;
    }
}

/// <summary>One container with 5,000 fictional documents per entity set for one owner plus 500 for another.</summary>
public sealed class BenchmarkFixture : IAsyncLifetime
{
    public const string Owner = "benchmark-owner-aaa";
    public const string OtherOwner = "benchmark-owner-bbb";
    public const int DocumentsPerSet = 5000;
    private const int OtherOwnerDocuments = 500;

    internal ODataTestHost Host { get; } = new("odata_benchmark_test_db", Owner, OtherOwner);

    public async ValueTask InitializeAsync()
    {
        await Host.StartDatabaseAsync();
        var db = Host.Database;

        await InsertAsync(db, TrackingMongoOptions.PostingsCollectionName, Postings);
        await InsertAsync(db, TrackingMongoOptions.CompaniesCollectionName, Companies);
        await InsertAsync(db, TrackingMongoOptions.ApplicationsCollectionName, Applications);
        await InsertAsync(db, MongoBootstrapAndIndexes.NotesCollectionName, Notes);
        await InsertAsync(db, TrackingMongoOptions.CriteriaCollectionName, Criteria);
        await InsertAsync(db, TrackingMongoOptions.SearchRunsCollectionName, SearchRuns);

        // No explicit index creation: the API host creates the indexes itself at startup (ReadIndexStartupService),
        // so this measures the real startup path.

        await Host.StartApiAsync();
    }

    public async ValueTask DisposeAsync() => await Host.DisposeAsync();

    private static async Task InsertAsync(IMongoDatabase db, string collection, Func<string, int, IEnumerable<BsonDocument>> make)
    {
        var target = db.GetCollection<BsonDocument>(collection);
        foreach (var batch in make(Owner, DocumentsPerSet).Concat(make(OtherOwner, OtherOwnerDocuments)).Chunk(1000))
        {
            await target.InsertManyAsync(batch);
        }
    }

    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static BsonBinaryData NewId() => new(Guid.CreateVersion7(), GuidRepresentation.Standard);

    private static string[] Dispositions => ["unscored", "lead", "scored", "applied", "rejected", "closed"];

    private static IEnumerable<BsonDocument> Postings(string owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var scored = i % 4 != 0;
            yield return new BsonDocument
            {
                ["_id"] = NewId(),
                ["owner_id"] = owner,
                ["company_id"] = NewId(),
                ["company_name"] = $"Company {i % 700}",
                ["title"] = i % 2 == 0 ? $"Engineer {i}" : $"manager {i}",
                ["requisition_id"] = $"REQ-{i}",
                ["location"] = "Remote",
                ["disposition"] = Dispositions[i % Dispositions.Length],
                ["disposition_rank"] = i % Dispositions.Length,
                ["resurface_policy"] = "never",
                ["score"] = scored ? new BsonInt32(i % 100) : BsonNull.Value,
                ["has_score"] = scored,
                ["created_at"] = new BsonDateTime(Epoch.AddMinutes(i)),
                ["updated_at"] = new BsonDateTime(Epoch.AddMinutes(i * 2))
            };
        }
    }

    private static IEnumerable<BsonDocument> Companies(string owner, int count)
    {
        string[] statuses = ["target", "active", "watching", "excluded"];
        string[] ownership = ["public", "private", "subsidiary", "nonprofit"];
        for (var i = 0; i < count; i++)
        {
            var name = i % 3 == 0 ? $"company {i}" : $"Company {i}";
            yield return new BsonDocument
            {
                ["_id"] = NewId(),
                ["owner_id"] = owner,
                ["name"] = name,
                ["normalized_name"] = name.ToLowerInvariant(),
                ["description"] = BsonNull.Value,
                ["parent_company"] = BsonNull.Value,
                ["ownership_type"] = ownership[i % ownership.Length],
                ["headquarters"] = "Remote",
                ["category_tags"] = new BsonArray(["fintech"]),
                ["status"] = statuses[i % statuses.Length],
                ["version_token"] = NewId(),
                ["created_at"] = new BsonDateTime(Epoch.AddMinutes(i)),
                ["updated_at"] = new BsonDateTime(Epoch.AddMinutes(i))
            };
        }
    }

    private static IEnumerable<BsonDocument> Applications(string owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var responded = i % 3 != 0;
            yield return new BsonDocument
            {
                ["_id"] = NewId(),
                ["owner_id"] = owner,
                ["posting_id"] = NewId(),
                ["company_id"] = NewId(),
                ["posting_title"] = $"Role {i}",
                ["company_name"] = $"Company {i % 700}",
                ["status"] = i % 5 == 0 ? "interviewing" : "applied",
                ["applied_at"] = new BsonDateTime(Epoch.AddHours(i)),
                ["has_interview"] = i % 5 == 0,
                ["first_response_at"] = responded ? new BsonDateTime(Epoch.AddHours(i + 30)) : BsonNull.Value,
                ["last_event_at"] = new BsonDateTime(Epoch.AddHours(i + 40)),
                ["has_first_response"] = responded,
                ["has_last_event"] = true
            };
        }
    }

    private static IEnumerable<BsonDocument> Notes(string owner, int count)
    {
        var body = new string('n', 2048);
        for (var i = 0; i < count; i++)
        {
            yield return new BsonDocument
            {
                ["_id"] = NewId(),
                ["owner_id"] = owner,
                ["title"] = i % 2 == 0 ? $"Note {i}" : $"note {i}",
                ["body"] = body,
                ["current_body_hash"] = "hash",
                ["tags"] = new BsonArray(["t1"]),
                ["area"] = i % 3 == 0 ? "interview" : "research",
                ["status"] = "active",
                ["version_token"] = NewId(),
                ["revision_count"] = 0,
                ["created_at"] = new BsonDateTime(Epoch.AddMinutes(i)),
                ["updated_at"] = new BsonDateTime(Epoch.AddMinutes(i * 2)),
                ["company_id"] = BsonNull.Value,
                ["posting_id"] = BsonNull.Value,
                ["application_id"] = BsonNull.Value
            };
        }
    }

    private static IEnumerable<BsonDocument> Criteria(string owner, int count)
    {
        string[] categories = ["hard_skip", "weight", "preference"];
        for (var i = 0; i < count; i++)
        {
            var bounded = i % 3 != 0;
            yield return new BsonDocument
            {
                ["_id"] = NewId(),
                ["owner_id"] = owner,
                ["key"] = i % 2 == 0 ? $"criterion {i}" : $"Criterion {i}",
                ["category"] = categories[i % categories.Length],
                ["value"] = "fictional rule text",
                ["effective_from"] = new BsonDateTime(Epoch.AddDays(i % 300)),
                ["effective_to"] = bounded ? new BsonDateTime(Epoch.AddDays(300 + i % 400)) : BsonNull.Value,
                ["has_effective_to"] = bounded,
                ["provenance"] = "stated",
                ["is_active"] = true,
                ["version_token"] = NewId(),
                ["created_at"] = new BsonDateTime(Epoch)
            };
        }
    }

    private static IEnumerable<BsonDocument> SearchRuns(string owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new BsonDocument
            {
                ["_id"] = NewId(),
                ["owner_id"] = owner,
                ["started_at"] = new BsonDateTime(Epoch.AddMinutes(i * 30)),
                ["new_postings_count"] = i % 12,
                ["report_note_id"] = BsonNull.Value,
                ["notes"] = i % 4 == 0 ? $"sweep {i}" : BsonNull.Value,
                ["created_at"] = new BsonDateTime(Epoch.AddMinutes(i * 30))
            };
        }
    }
}
