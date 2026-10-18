using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Infrastructure.Mongo.Repositories;

public sealed class MongoSearchRunRepository : ISearchRunRepository
{
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _searchRuns;

    public MongoSearchRunRepository(IMongoClient client, IMongoDatabase database)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _searchRuns = database.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName);
    }

    public async Task<Result<SearchRun?>> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(id, GuidRepresentation.Standard)));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _searchRuns.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _searchRuns.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<SearchRun?>.Success(doc is null ? null : MapSearchRun(doc));
    }

    public async Task<Result<SearchRun?>> GetByStartedAtAsync(string ownerId, DateTimeOffset startedAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", ownerId),
            Builders<BsonDocument>.Filter.Eq("started_at", new BsonDateTime(startedAt.UtcDateTime)));

        var session = MongoNoteRepository.CurrentSession;
        var doc = session is not null
            ? await _searchRuns.Find(session, filter).FirstOrDefaultAsync(cancellationToken)
            : await _searchRuns.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return Result<SearchRun?>.Success(doc is null ? null : MapSearchRun(doc));
    }

    public async Task<Result<bool>> UpsertSearchRunAsync(SearchRun searchRun, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(searchRun);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);

        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("owner_id", searchRun.OwnerId),
            Builders<BsonDocument>.Filter.Eq("_id", new BsonBinaryData(searchRun.Id, GuidRepresentation.Standard)));

        var doc = SerializeSearchRun(searchRun);
        var session = MongoNoteRepository.CurrentSession;
        try
        {
            var result = session is not null
                ? await _searchRuns.ReplaceOneAsync(session, filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken)
                : await _searchRuns.ReplaceOneAsync(filter, doc, new ReplaceOptions { IsUpsert = true }, cancellationToken);
            return Result<bool>.Success(result.IsAcknowledged);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return Result<bool>.Failure(ErrorCode.ValidationError, "Duplicate search run key.");
        }
    }

    public async Task<Result<IReadOnlyList<SearchRun>>> ListSearchRunsAsync(string ownerId, int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.Eq("owner_id", ownerId);
        var session = MongoNoteRepository.CurrentSession;
        var docs = session is not null
            ? await _searchRuns.Find(session, filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("started_at"))
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync(cancellationToken)
            : await _searchRuns.Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("started_at"))
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<SearchRun>>.Success(docs.Select(MapSearchRun).ToArray());
    }

    public async Task<Result<int>> CountSearchRunsAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        await TrackingIndexInitializer.EnsureIndexesOnceAsync(_database, cancellationToken);
        var filter = Builders<BsonDocument>.Filter.Eq("owner_id", ownerId);
        var session = MongoNoteRepository.CurrentSession;
        var count = session is not null
            ? await _searchRuns.CountDocumentsAsync(session, filter, cancellationToken: cancellationToken)
            : await _searchRuns.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        return Result<int>.Success((int)count);
    }

    private static BsonDocument SerializeSearchRun(SearchRun run) =>
        new()
        {
            { "_id", new BsonBinaryData(run.Id, GuidRepresentation.Standard) },
            { "owner_id", run.OwnerId },
            { "started_at", new BsonDateTime(run.StartedAt.UtcDateTime) },
            { "new_postings_count", run.NewPostingsCount },
            { "report_note_id", run.ReportNoteId.HasValue ? new BsonBinaryData(run.ReportNoteId.Value, GuidRepresentation.Standard) : BsonNull.Value },
            { "notes", run.Notes is not null ? new BsonString(run.Notes) : BsonNull.Value },
            { "created_at", new BsonDateTime(run.CreatedAt.UtcDateTime) }
        };

    private static SearchRun MapSearchRun(BsonDocument doc)
    {
        var id = doc["_id"].AsBsonBinaryData.ToGuid(GuidRepresentation.Standard);
        var ownerId = doc["owner_id"].AsString;
        var startedAt = new DateTimeOffset(doc["started_at"].ToUniversalTime(), TimeSpan.Zero);
        var newPostingsCount = doc["new_postings_count"].AsInt32;
        Guid? reportNoteId = doc.TryGetValue("report_note_id", out var rVal) && !rVal.IsBsonNull && rVal.IsBsonBinaryData
            ? rVal.AsBsonBinaryData.ToGuid(GuidRepresentation.Standard)
            : null;
        string? notes = doc.TryGetValue("notes", out var nVal) && !nVal.IsBsonNull ? nVal.AsString : null;
        var createdAt = new DateTimeOffset(doc["created_at"].ToUniversalTime(), TimeSpan.Zero);

        return new SearchRun(id, ownerId, startedAt, newPostingsCount, reportNoteId, notes, createdAt);
    }
}
