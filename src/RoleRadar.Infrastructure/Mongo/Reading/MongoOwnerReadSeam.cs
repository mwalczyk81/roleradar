namespace RoleRadar.Infrastructure.Mongo.Reading;

using MongoDB.Driver;
using RoleRadar.Application.Reading;
using RoleRadar.Application.Reading.Models;
using RoleRadar.Domain.Common;

public sealed class MongoOwnerReadSeam : IOwnerReadSeam
{
    private readonly IMongoDatabase _database;
    private readonly ReadIndexInitializer? _indexes;
    private static readonly AggregateOptions DefaultAggregateOptions = new()
    {
        Collation = new Collation("en", strength: CollationStrength.Secondary)
    };

    /// <param name="indexes">
    /// When given, every query entry first makes sure the read indexes exist (a no-op once they do), so an API whose
    /// startup attempt failed still creates them on the first read after Mongo is back.
    /// </param>
    public MongoOwnerReadSeam(IMongoDatabase database, ReadIndexInitializer? indexes = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _indexes = indexes;
        ReadModelClassMaps.Register();
    }

    public IQueryable<PostingReadModel> QueryPostings(OwnerId ownerId)
    {
        _indexes?.TryEnsureOnRead();
        var collection = _database.GetCollection<PostingReadModel>(TrackingMongoOptions.PostingsCollectionName);
        return collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
    }

    public IQueryable<CompanyReadModel> QueryCompanies(OwnerId ownerId)
    {
        _indexes?.TryEnsureOnRead();
        var collection = _database.GetCollection<CompanyReadModel>(TrackingMongoOptions.CompaniesCollectionName);
        return collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
    }

    public IQueryable<ApplicationReadModel> QueryApplications(OwnerId ownerId)
    {
        _indexes?.TryEnsureOnRead();
        var collection = _database.GetCollection<ApplicationReadModel>(TrackingMongoOptions.ApplicationsCollectionName);
        return collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
    }

    public IQueryable<NoteReadModel> QueryNotes(OwnerId ownerId, bool includeContent = false)
    {
        _indexes?.TryEnsureOnRead();
        var collection = _database.GetCollection<NoteReadModel>(MongoBootstrapAndIndexes.NotesCollectionName);
        var owned = collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
        if (includeContent)
        {
            return owned;
        }

        // Explicit member projection: MongoDB receives a $project that simply never names "body".
        return owned.Select(n => new NoteReadModel
        {
            Id = n.Id,
            Heading = n.Heading,
            Tags = n.Tags,
            LinkedEntityType = n.LinkedEntityType,
            CreatedAt = n.CreatedAt,
            UpdatedAt = n.UpdatedAt
        });
    }

    public IQueryable<CriterionReadModel> QueryCriteria(OwnerId ownerId)
    {
        _indexes?.TryEnsureOnRead();
        var collection = _database.GetCollection<CriterionReadModel>(TrackingMongoOptions.CriteriaCollectionName);
        return collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
    }

    public IQueryable<SearchRunReadModel> QuerySearchRuns(OwnerId ownerId)
    {
        _indexes?.TryEnsureOnRead();
        var collection = _database.GetCollection<SearchRunReadModel>(TrackingMongoOptions.SearchRunsCollectionName);
        return collection.AsQueryable(DefaultAggregateOptions).Where(p => p.OwnerId == ownerId.Value);
    }
}
