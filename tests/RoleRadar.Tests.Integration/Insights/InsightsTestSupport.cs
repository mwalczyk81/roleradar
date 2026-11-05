namespace RoleRadar.Tests.Integration.Insights;

using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;

/// <summary>Helpers shared by the Insights tests: raw document access and OData counts through the real API.</summary>
internal static class InsightsTestSupport
{
    public static BsonBinaryData Bin(Guid id) => new(id, GuidRepresentation.Standard);

    public static async Task<BsonDocument> PostingDocAsync(IMongoDatabase db, string key) =>
        await db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", Bin(InsightsSeed.PostingId(key)))).FirstAsync();

    public static async Task<BsonDocument> ApplicationDocAsync(IMongoDatabase db, string key) =>
        await db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName)
            .Find(Builders<BsonDocument>.Filter.Eq("_id", Bin(InsightsSeed.ApplicationId(key)))).FirstAsync();

    /// <summary>The value of a boolean field, or null when the field is absent or null.</summary>
    public static bool? Flag(BsonDocument doc, string name) =>
        doc.Contains(name) && doc[name].IsBoolean ? doc[name].AsBoolean : null;

    /// <summary>UTC midnight literal for a date, in the form OData expects.</summary>
    public static string Literal(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "T00:00:00Z";

    /// <summary>The 90-day window clause for an entity date field, as the web lens module will build it.</summary>
    public static string WindowClause(string field, DateOnly asOf, bool last90Days) =>
        last90Days
            ? $"{field} ge {Literal(asOf.AddDays(-89))} and {field} lt {Literal(asOf.AddDays(1))}"
            : $"{field} lt {Literal(asOf.AddDays(1))}";

    public static async Task<int> CountAsync(ODataTestHost host, string owner, string entitySet, string filter)
    {
        var path = $"/odata/{entitySet}?$filter={Uri.EscapeDataString(filter)}&$top=1&$select=id&$count=true";
        var (status, body) = await host.GetJsonAsync(owner, path);
        using (body)
        {
            if (status != System.Net.HttpStatusCode.OK)
            {
                throw new InvalidOperationException($"{path} returned {(int)status}: {body.RootElement}");
            }

            return body.RootElement.GetProperty("@odata.count").GetInt32();
        }
    }

    public static async Task<(System.Net.HttpStatusCode Status, string Body)> RawAsync(ODataTestHost host, string owner, string pathAndQuery)
    {
        using var response = await host.GetAsync(owner, pathAndQuery);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    public static JsonElement ParseRoot(string body) => JsonDocument.Parse(body).RootElement.Clone();
}
