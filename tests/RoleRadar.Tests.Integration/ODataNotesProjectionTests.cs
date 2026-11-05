namespace RoleRadar.Tests.Integration;

using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

/// <summary>
/// Phase 5 (T049): the notes list must never pull note bodies out of MongoDB unless <c>$select</c> asks for
/// <c>content</c>. Proven with the server-side profiler (pipeline shape and response size), not only by
/// looking at the JSON the API returns.
/// </summary>
public sealed class ODataNotesProjectionTests : IAsyncLifetime
{
    private const string OwnerA = "projection-owner-aaa";
    private const int BodyBytes = 200_000;
    private const int NoteCount = 3;

    private readonly ODataTestHost _host = new("odata_notes_projection_test_db", OwnerA);

    public async ValueTask InitializeAsync()
    {
        await _host.StartDatabaseAsync();

        var notes = _host.Database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.NotesCollectionName);
        for (var i = 1; i <= NoteCount; i++)
        {
            await notes.InsertOneAsync(new BsonDocument
            {
                ["_id"] = new BsonBinaryData(Guid.Parse($"{i:D8}-0000-0000-0000-000000000000"), GuidRepresentation.Standard),
                ["owner_id"] = OwnerA,
                ["title"] = $"Projection note {i}",
                ["body"] = new string((char)('a' + i), BodyBytes),
                ["current_body_hash"] = "hash",
                ["tags"] = new BsonArray(["t"]),
                ["area"] = "area",
                ["status"] = "active",
                ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
                ["revision_count"] = 0,
                ["created_at"] = new BsonDateTime(new DateTime(2026, 10, i, 0, 0, 0, DateTimeKind.Utc)),
                ["updated_at"] = new BsonDateTime(new DateTime(2026, 10, i, 0, 0, 0, DateTimeKind.Utc))
            });
        }

        await _host.StartApiAsync();

        // Level 2 profiles every operation on this (container-local) test database.
        await _host.Database.RunCommandAsync<BsonDocument>(new BsonDocument { ["profile"] = 2, ["slowms"] = 0 });
    }

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    private sealed record NotesAggregate(string PipelineJson, long ResponseLength);

    private async Task<NotesAggregate> LastNotesAggregateAsync()
    {
        var profile = _host.Database.GetCollection<BsonDocument>("system.profile");
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("ns", $"{_host.Database.DatabaseNamespace.DatabaseName}.{MongoBootstrapAndIndexes.NotesCollectionName}"),
            Builders<BsonDocument>.Filter.Exists("command.pipeline"),
            Builders<BsonDocument>.Filter.Exists("command.aggregate"));

        BsonDocument? entry = null;
        for (var attempt = 0; attempt < 20 && entry is null; attempt++)
        {
            entry = await profile.Find(filter).Sort(Builders<BsonDocument>.Sort.Descending("ts")).FirstOrDefaultAsync();
            if (entry is null)
            {
                await Task.Delay(100);
            }
        }

        Assert.NotNull(entry);
        return new NotesAggregate(entry!["command"]["pipeline"].ToJson(), entry["responseLength"].ToInt64());
    }

    private static JsonElement Rows(JsonDocument body)
    {
        Assert.False(body.RootElement.TryGetProperty("items", out _), "responses use the OData 'value' envelope only");
        return body.RootElement.GetProperty("value");
    }

    private async Task ClearProfileAsync()
    {
        var db = _host.Database;
        await db.RunCommandAsync<BsonDocument>(new BsonDocument { ["profile"] = 0 });
        await db.DropCollectionAsync("system.profile");
        await db.RunCommandAsync<BsonDocument>(new BsonDocument { ["profile"] = 2, ["slowms"] = 0 });
    }

    [Fact]
    public async Task DefaultNotesList_ProjectsContentAway_InTheDatabaseQuery()
    {
        await ClearProfileAsync();

        var (status, body) = await _host.GetJsonAsync(OwnerA, "/odata/Notes?$orderby=updatedAt%20desc");
        Assert.Equal(HttpStatusCode.OK, status);

        var rows = Rows(body).EnumerateArray().ToList();
        Assert.Equal(NoteCount, rows.Count);
        foreach (var row in rows)
        {
            Assert.True(
                !row.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null,
                "content must be null or omitted from the default list");
            Assert.StartsWith("Projection note", row.GetProperty("heading").GetString());
        }

        var aggregate = await LastNotesAggregateAsync();
        Assert.Contains("$project", aggregate.PipelineJson);
        Assert.DoesNotContain("body", aggregate.PipelineJson);
        Assert.True(
            aggregate.ResponseLength < BodyBytes,
            $"default notes query returned {aggregate.ResponseLength} bytes from MongoDB; bodies were not projected away");
    }

    [Fact]
    public async Task ExplicitSelectOfContent_FetchesBodies_FromTheDatabase()
    {
        await ClearProfileAsync();

        var (status, body) = await _host.GetJsonAsync(OwnerA, "/odata/Notes?$select=heading,content&$orderby=updatedAt%20desc");
        Assert.Equal(HttpStatusCode.OK, status);

        var first = Rows(body)[0];
        Assert.Equal(BodyBytes, first.GetProperty("content").GetString()!.Length);

        var aggregate = await LastNotesAggregateAsync();
        Assert.True(
            aggregate.ResponseLength >= BodyBytes * NoteCount,
            $"$select=content should read the bodies, but MongoDB returned only {aggregate.ResponseLength} bytes");
    }

    [Fact]
    public async Task SelectWithoutContent_StillProjectsContentAway_InTheDatabaseQuery()
    {
        await ClearProfileAsync();

        var (status, body) = await _host.GetJsonAsync(OwnerA, "/odata/Notes?$select=heading,updatedAt");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(Rows(body)[0].TryGetProperty("content", out _));

        var aggregate = await LastNotesAggregateAsync();
        Assert.True(aggregate.ResponseLength < BodyBytes, $"{aggregate.ResponseLength} bytes returned");
    }
}
