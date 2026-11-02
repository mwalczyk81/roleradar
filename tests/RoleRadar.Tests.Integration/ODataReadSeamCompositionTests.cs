namespace RoleRadar.Tests.Integration;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using Microsoft.OData.UriParser;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Reading;
using RoleRadar.Application.Reading.Models;
using RoleRadar.Domain.Common;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Infrastructure.Mongo.Reading;
using Testcontainers.MongoDb;
using Xunit;

public sealed class ODataReadSeamCompositionTests : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:8.0.34")
        .WithEnvironment("GLIBC_TUNABLES", "glibc.pthread.rseq=1")
        .WithReplicaSet("rs0")
        .Build();

    private IMongoDatabase _database = null!;
    private readonly OwnerId _primaryOwner = new("owner-primary-1111");
    private readonly OwnerId _secondOwner = new("owner-secondary-2222");

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var client = new MongoClient(_container.GetConnectionString());
        _database = client.GetDatabase("odata_read_seam_tests");
        await SeedDataAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private async Task SeedDataAsync()
    {
        var collection = _database.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName);

        // Seed primary owner docs: 6 matching "engineer" (4 with score, 2 without score)
        // Scores: 50, 75, 85, 95, null, null
        var docs = new List<BsonDocument>
        {
            CreateDoc(_primaryOwner.Value, "Senior Software Engineer", 85, true, "applied", 3),
            CreateDoc(_primaryOwner.Value, "junior software engineer", 50, true, "lead", 1),
            CreateDoc(_primaryOwner.Value, "STAFF ENGINEER", 95, true, "scored", 2),
            CreateDoc(_primaryOwner.Value, "Security Engineer", null, false, "unscored", 0),
            CreateDoc(_primaryOwner.Value, "Cloud Engineer", 75, true, "lead", 1),
            CreateDoc(_primaryOwner.Value, "QA Engineer", null, false, "applied", 3),

            // Non-matching docs for primary owner
            CreateDoc(_primaryOwner.Value, "Product Manager", 90, true, "applied", 3),
            CreateDoc(_primaryOwner.Value, "Data Analyst", null, false, "lead", 1),

            // Non-matching and matching docs for second owner (to assert multi-owner isolation)
            CreateDoc(_secondOwner.Value, "Lead Software Engineer", 99, true, "applied", 3),
            CreateDoc(_secondOwner.Value, "Platform Engineer", null, false, "lead", 1)
        };

        await collection.InsertManyAsync(docs);
    }

    private static BsonDocument CreateDoc(string ownerId, string title, int? score, bool hasScore, string disposition, int dispositionRank)
    {
        var id = Guid.NewGuid();
        return new BsonDocument
        {
            ["_id"] = new BsonBinaryData(id, GuidRepresentation.Standard),
            ["owner_id"] = ownerId,
            ["company_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["company_name"] = "Acme Corp",
            ["title"] = title,
            ["requisition_id"] = "REQ-001",
            ["location"] = "Remote",
            ["disposition"] = disposition,
            ["disposition_rank"] = dispositionRank,
            ["resurface_policy"] = "never",
            ["score"] = score.HasValue ? score.Value : BsonNull.Value,
            ["has_score"] = hasScore,
            ["created_at"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at"] = new BsonDateTime(DateTime.UtcNow)
        };
    }

    private static IEdmModel GetEdmModel()
    {
        var builder = new ODataConventionModelBuilder();
        builder.EntitySet<PostingReadModel>("Postings");
        return builder.GetEdmModel();
    }

    private static ODataQueryOptions<PostingReadModel> CreateODataQueryOptions(string queryString)
    {
        var model = GetEdmModel();
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString(queryString);

        var edmSet = model.EntityContainer.FindEntitySet("Postings");
        var path = new ODataPath(new EntitySetSegment(edmSet));
        var odataContext = new ODataQueryContext(model, typeof(PostingReadModel), path);
        return new ODataQueryOptions<PostingReadModel>(odataContext, context.Request);
    }

    [Fact]
    public void Test_ReadSeam_Composition_Filter_NullsLast_Paging_Count()
    {
        var seam = new MongoOwnerReadSeam(_database);
        var baseQuery = seam.QueryPostings(_primaryOwner);

        // OData Query: filter by contains(tolower(Title), 'engineer'), $orderby=score asc, $skip=2, $top=2, $count=true
        var options = CreateODataQueryOptions("?$filter=contains(tolower(Title), 'engineer')&$orderby=score asc&$top=2&$skip=2&$count=true");
        var settings = new ODataQuerySettings { HandleNullPropagation = HandleNullPropagationOption.False };

        // 1. Filter composition over LINQ
        var filtered = (IQueryable<PostingReadModel>)(options.Filter?.ApplyTo(baseQuery, settings) ?? baseQuery);

        // 2. Total Count evaluation against filtered query
        var totalCount = filtered.Count();
        Assert.Equal(6, totalCount);

        // 3. Nulls-last Sort composition: has_score descending (-1), score ascending (1), Id ascending (1)
        var ordered = filtered
            .OrderByScore(descending: false)
            .ThenBy(p => p.Id);

        // 4. Paging composition: Skip 2, Take 2
        var paged = (IQueryable<PostingReadModel>)options.Top.ApplyTo(
            options.Skip.ApplyTo(ordered, settings),
            settings);

        // 5. Materialize page 2
        var page2Results = paged.ToList();

        // Verification:
        // Matching sorted items in order across all 6 records:
        // - Item 0: score 50 (junior software engineer)
        // - Item 1: score 75 (Cloud Engineer)
        // - Item 2 (Page 2 start): score 85 (Senior Software Engineer)
        // - Item 3: score 95 (STAFF ENGINEER)
        // - Item 4: null score
        // - Item 5: null score
        Assert.Equal(2, page2Results.Count);
        Assert.Equal(85, page2Results[0].Score);
        Assert.Equal("Senior Software Engineer", page2Results[0].Title);
        Assert.Equal(95, page2Results[1].Score);
        Assert.Equal("STAFF ENGINEER", page2Results[1].Title);

        // Multi-owner verification: ensure zero documents from second owner leaked into results
        Assert.DoesNotContain(page2Results, p => p.Title.Contains("Platform", StringComparison.OrdinalIgnoreCase));
    }
}
