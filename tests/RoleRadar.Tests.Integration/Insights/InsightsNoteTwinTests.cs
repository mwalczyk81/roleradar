namespace RoleRadar.Tests.Integration.Insights;

using System.Globalization;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Reconcile;
using Xunit;

/// <summary>
/// Feature 008, FR-013 and SC-003: note events change no Insights figure. The same fictional seed is written twice
/// into two isolated Testcontainers databases, once with its note events and once without, and every figure the
/// dashboard can show is read through the real API from both and compared. Nothing here changes an Insights
/// definition; the oracle is the existing one and does not know about notes.
/// </summary>
public sealed class InsightsNoteTwinTests(InsightsFixture withNotes, InsightsNoNotesFixture withoutNotes)
    : IClassFixture<InsightsFixture>, IClassFixture<InsightsNoNotesFixture>
{
    private static readonly string AsOfText = InsightsSeed.FixedAsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static readonly string[] DerivedFields =
    [
        "outcome", "has_first_response", "first_response_at", "has_interview", "has_offer", "has_rejection",
        "rejected_before_interview", "has_last_event", "last_event_at"
    ];

    [Fact]
    public async Task TheTwinsDiffer_OnlyByTheNoteEvents()
    {
        var notesWith = await NoteEventCountAsync(withNotes.Database);
        var notesWithout = await NoteEventCountAsync(withoutNotes.Database);

        Assert.True(InsightsSeed.NoteEventCount >= 5, "the seed must carry note events, including an old silent application");
        Assert.Equal(InsightsSeed.NoteEventCount, notesWith);
        Assert.Equal(0, notesWithout);

        var eventsWith = await EventCountAsync(withNotes.Database);
        var eventsWithout = await EventCountAsync(withoutNotes.Database);
        Assert.Equal(eventsWithout + notesWith, eventsWith);
    }

    [Fact]
    public async Task EveryLensCount_ForEveryOwnerAndWindow_IsIdentical_WithAndWithoutNotes()
    {
        var snapshot = InsightsLensSnapshot.Load();
        var figuresWith = new List<string>();
        var figuresWithout = new List<string>();

        foreach (var owner in InsightsSeed.AllOwners)
        {
            foreach (var key in InsightsOracle.LensKeys)
            {
                foreach (var last90Days in new[] { true, false })
                {
                    var filter = InsightsLensSnapshot.Filter(snapshot, "lenses", key, last90Days);
                    var set = InsightsLensSnapshot.EntitySet(InsightsLensSnapshot.Entity(snapshot, "lenses", key));

                    var with = await InsightsTestSupport.CountAsync(withNotes.Host, owner, set, filter);
                    var without = await InsightsTestSupport.CountAsync(withoutNotes.Host, owner, set, filter);

                    figuresWith.Add($"{owner}|{key}|{(last90Days ? "90d" : "all")}={with}");
                    figuresWithout.Add($"{owner}|{key}|{(last90Days ? "90d" : "all")}={without}");

                    // Both also agree with the independent oracle, which ignores notes by construction.
                    Assert.Equal(InsightsOracle.Count(owner, key, last90Days), with);
                }
            }
        }

        Assert.Equal(InsightsSeed.AllOwners.Length * InsightsOracle.LensKeys.Length * 2, figuresWith.Count);
        Assert.Equal(figuresWithout, figuresWith);
    }

    [Theory]
    [InlineData("last90Days")]
    [InlineData("allTime")]
    public async Task StatsRead_ResponseTimeWeeklyBucketsAndRunYield_AreIdentical_WithAndWithoutNotes(string window)
    {
        foreach (var owner in new[] { InsightsSeed.OwnerA, InsightsSeed.OwnerB, InsightsSeed.OwnerD })
        {
            var path = $"/api/v1/insights/stats?window={window}&asOf={AsOfText}";
            var (statusWith, bodyWith) = await InsightsTestSupport.RawAsync(withNotes.Host, owner, path);
            var (statusWithout, bodyWithout) = await InsightsTestSupport.RawAsync(withoutNotes.Host, owner, path);

            Assert.Equal(System.Net.HttpStatusCode.OK, statusWith);
            Assert.Equal(System.Net.HttpStatusCode.OK, statusWithout);
            Assert.Equal(Canonical(bodyWithout), Canonical(bodyWith));
        }
    }

    [Fact]
    public async Task EveryApplication_HasTheSameDerivedFields_WithAndWithoutNotes()
    {
        var problems = new List<string>();
        foreach (var seed in InsightsSeed.Postings.Where(p => p.HasApplication))
        {
            var with = await InsightsTestSupport.ApplicationDocAsync(withNotes.Database, seed.Key);
            var without = await InsightsTestSupport.ApplicationDocAsync(withoutNotes.Database, seed.Key);

            foreach (var field in DerivedFields)
            {
                var a = with.Contains(field) ? with[field] : BsonNull.Value;
                var b = without.Contains(field) ? without[field] : BsonNull.Value;
                if (!a.Equals(b))
                {
                    problems.Add($"{seed.Key}.{field}: with notes {a}, without {b}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public async Task OldSilentApplicationWithANote_IsStillGhosted_StillHasNoResponse_AndStillOpen()
    {
        var snapshot = InsightsLensSnapshot.Load();
        var id = InsightsSeed.ApplicationId("age-50-silent");

        foreach (var lens in new[] { "ghosted", "open" })
        {
            var filter = InsightsLensSnapshot.Filter(snapshot, "lenses", lens, last90Days: false);
            var (status, body) = await withNotes.Host.GetJsonAsync(
                InsightsLensSnapshot.Owner, $"/odata/Applications?$filter={Uri.EscapeDataString(filter)}&$select=id&$top=100");
            using (body)
            {
                Assert.Equal(System.Net.HttpStatusCode.OK, status);
                var ids = body.RootElement.GetProperty("value").EnumerateArray()
                    .Select(r => Guid.Parse(r.GetProperty("id").GetString()!)).ToHashSet();
                Assert.Contains(id, ids);
            }
        }

        var doc = await InsightsTestSupport.ApplicationDocAsync(withNotes.Database, "age-50-silent");
        Assert.Equal(false, InsightsTestSupport.Flag(doc, "has_first_response"));
        Assert.Equal("applied", doc["outcome"].AsString);
    }

    [Fact]
    public async Task CorrectionNoteAfterARejection_DoesNotAddASecondRejection()
    {
        var doc = await InsightsTestSupport.ApplicationDocAsync(withNotes.Database, "rej-no-interview");

        Assert.Equal(true, InsightsTestSupport.Flag(doc, "has_rejection"));
        var events = await withNotes.Database.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName)
            .Find(Builders<BsonDocument>.Filter.Eq("application_id", InsightsTestSupport.Bin(InsightsSeed.ApplicationId("rej-no-interview"))))
            .ToListAsync();
        Assert.Equal(1, events.Count(e => e["event_type"].AsString == "rejected"));
        Assert.Equal(1, events.Count(e => e["event_type"].AsString == "note"));
    }

    [Fact]
    public async Task ReconcileDryRun_OnADatabaseWithNotes_ReportsNothingToFix()
    {
        // The reconcile service shares the milestone code with the repository, so notes must be inert there too.
        var service = new ReadModelReconciliationService(withNotes.Database);

        var postings = await service.ReconcilePostingsAsync(dryRun: true);
        var applications = await service.ReconcileApplicationsAsync(dryRun: true);

        Assert.Equal(0, postings.PostingsUpdated);
        Assert.Equal(0, applications.ApplicationsUpdated);
        Assert.Equal(InsightsSeed.Postings.Count(p => p.HasApplication), applications.ApplicationsExamined);
    }

    private static async Task<long> NoteEventCountAsync(IMongoDatabase db) =>
        await db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName)
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("event_type", "note"));

    private static async Task<long> EventCountAsync(IMongoDatabase db) =>
        await db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationEventsCollectionName)
            .CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);

    private static string Canonical(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement);
    }
}
