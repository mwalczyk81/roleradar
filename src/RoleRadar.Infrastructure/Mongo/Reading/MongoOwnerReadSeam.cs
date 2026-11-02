namespace RoleRadar.Infrastructure.Mongo.Reading;

using MongoDB.Driver;
using RoleRadar.Application.Reading;
using RoleRadar.Application.Reading.Models;
using RoleRadar.Domain.Common;

public sealed class MongoOwnerReadSeam : IOwnerReadSeam
{
    private readonly IMongoDatabase _database;
    private static readonly AggregateOptions DefaultAggregateOptions = new()
    {
        Collation = new Collation("en", strength: CollationStrength.Secondary)
    };

    public MongoOwnerReadSeam(IMongoDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        ReadModelClassMaps.Register();
    }

    public IQueryable<PostingReadModel> QueryPostings(OwnerId ownerId)
    {
        var collection = _database.GetCollection<PostingReadModel>(TrackingMongoOptions.PostingsCollectionName);
        return collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
    }

    public IQueryable<CompanyReadModel> QueryCompanies(OwnerId ownerId)
    {
        var collection = _database.GetCollection<CompanyReadModel>(TrackingMongoOptions.CompaniesCollectionName);
        return collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
    }

    public IQueryable<ApplicationReadModel> QueryApplications(OwnerId ownerId)
    {
        var collection = _database.GetCollection<ApplicationReadModel>(TrackingMongoOptions.ApplicationsCollectionName);
        return collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
    }

    public IQueryable<NoteReadModel> QueryNotes(OwnerId ownerId)
    {
        var collection = _database.GetCollection<NoteReadModel>(MongoBootstrapAndIndexes.NotesCollectionName);
        return collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
    }

    public IQueryable<CriterionReadModel> QueryCriteria(OwnerId ownerId)
    {
        var collection = _database.GetCollection<CriterionReadModel>(TrackingMongoOptions.CriteriaCollectionName);
        return collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
    }

    public IQueryable<SearchRunReadModel> QuerySearchRuns(OwnerId ownerId)
    {
        var collection = _database.GetCollection<SearchRunReadModel>(TrackingMongoOptions.SearchRunsCollectionName);
        return collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
    }
}
