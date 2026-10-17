using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Driver;

namespace RoleRadar.Infrastructure.Mongo;

/// <summary>
/// Structural append-only wrapper over an <see cref="IMongoCollection{BsonDocument}"/> for
/// <c>note_revisions</c> and <c>audit_log</c>. Exposes exclusively insert and find operations
/// (no delete, update, replace, or bulk-write members), ensuring no code path can mutate or
/// delete append-only records.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Wraps an IMongoCollection<BsonDocument> to expose only append-only insert and find operations.")]
public sealed class AppendOnlyMongoCollection
{
    private readonly IMongoCollection<BsonDocument> _inner;

    public AppendOnlyMongoCollection(IMongoDatabase database, string collectionName)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collectionName);
        _inner = database.GetCollection<BsonDocument>(collectionName);
    }

    public Task InsertOneAsync(
        IClientSessionHandle session,
        BsonDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(document);
        return _inner.InsertOneAsync(session, document, cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<BsonDocument>> FindSortedAsync(
        FilterDefinition<BsonDocument> filter,
        SortDefinition<BsonDocument> sort,
        IClientSessionHandle? session = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(sort);

        var cursor = session is not null
            ? _inner.Find(session, filter).Sort(sort)
            : _inner.Find(filter).Sort(sort);

        return await cursor.ToListAsync(cancellationToken);
    }
}
