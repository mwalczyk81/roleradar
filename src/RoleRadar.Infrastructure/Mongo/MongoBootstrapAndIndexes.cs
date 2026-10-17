using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace RoleRadar.Infrastructure.Mongo;

public static class MongoBootstrapAndIndexes
{
    public const string NotesCollectionName = "notes";
    public const string NoteRevisionsCollectionName = "note_revisions";
    public const string AuditLogCollectionName = "audit_log";

    private static int _serializersRegistered;

    public static void RegisterSerializers()
    {
        if (Interlocked.Exchange(ref _serializersRegistered, 1) == 1)
        {
            return;
        }

        try
        {
            BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
        }
        catch (BsonSerializationException)
        {
            // Already registered with GuidRepresentation.Standard in this AppDomain.
        }
    }

    public static async Task EnsureIndexesAsync(
        IMongoDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        RegisterSerializers();

        var notes = database.GetCollection<BsonDocument>(NotesCollectionName);
        var revisions = database.GetCollection<BsonDocument>(NoteRevisionsCollectionName);
        var audits = database.GetCollection<BsonDocument>(AuditLogCollectionName);

        var noteIndexes = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("owner_id")
                    .Ascending("import_metadata.import_key"),
                new CreateIndexOptions<BsonDocument>
                {
                    Name = "ux_notes_owner_import_key",
                    Unique = true,
                    PartialFilterExpression = new BsonDocument(
                        "import_metadata.import_key",
                        new BsonDocument("$type", "string"))
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Text("title")
                    .Text("body")
                    .Ascending("owner_id")
                    .Ascending("status")
                    .Ascending("area"),
                new CreateIndexOptions
                {
                    Name = "ix_notes_fulltext",
                    Weights = new BsonDocument
                    {
                        { "title", 10 },
                        { "body", 1 }
                    },
                    DefaultLanguage = "english"
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("owner_id")
                    .Ascending("status")
                    .Ascending("area")
                    .Ascending("tags")
                    .Descending("updated_at")
                    .Ascending("_id"),
                new CreateIndexOptions
                {
                    Name = "ix_notes_owner_status_area_tags_updated"
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("owner_id")
                    .Ascending("import_metadata.source_file_id")
                    .Ascending("import_metadata.section_order_index"),
                new CreateIndexOptions
                {
                    Name = "ix_notes_import_reassembly"
                })
        };

        var revisionIndexes = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("owner_id")
                    .Ascending("note_id")
                    .Ascending("revision_number"),
                new CreateIndexOptions
                {
                    Name = "ux_note_revisions_note_revnum",
                    Unique = true
                })
        };

        var auditIndexes = new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("owner_id")
                    .Ascending("target_note_id")
                    .Ascending("occurred_at")
                    .Ascending("_id"),
                new CreateIndexOptions
                {
                    Name = "ix_audit_log_target_note"
                }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("owner_id")
                    .Descending("occurred_at")
                    .Ascending("_id"),
                new CreateIndexOptions
                {
                    Name = "ix_audit_log_chronological"
                })
        };

        await notes.Indexes.CreateManyAsync(noteIndexes, cancellationToken);
        await revisions.Indexes.CreateManyAsync(revisionIndexes, cancellationToken);
        await audits.Indexes.CreateManyAsync(auditIndexes, cancellationToken);
    }
}
