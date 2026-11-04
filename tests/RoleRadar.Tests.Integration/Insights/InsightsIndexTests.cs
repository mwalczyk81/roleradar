namespace RoleRadar.Tests.Integration.Insights;

using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

public sealed class InsightsIndexTests(InsightsFixture fixture) : IClassFixture<InsightsFixture>
{
    [Theory]
    [InlineData("ix_postings_owner_has_application", "owner_id", "has_application", "_id")]
    [InlineData("ix_postings_owner_first_seen_at", "owner_id", "first_seen_at", "_id")]
    public async Task PostingIndexes_Exist_WithOwnerFirst(string name, string first, string second, string third)
    {
        var indexes = await fixture.Database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName)
            .Indexes.List().ToListAsync();

        var index = indexes.SingleOrDefault(i => i["name"].AsString == name);
        Assert.True(index is not null, $"Missing index {name}");
        Assert.Equal(new[] { first, second, third }, index!["key"].AsBsonDocument.Names.ToArray());
        Assert.Equal(1, index["key"]["owner_id"].ToInt32());
    }
}
