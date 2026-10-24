using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Mongo.Repositories;

namespace RoleRadar.Infrastructure.Mongo;

public sealed class MongoTrackingContextFactory : ITrackingContextFactory
{
    private readonly IMongoClient _client;
    private readonly MongoOptions _options;

    public MongoTrackingContextFactory(IMongoClient client, IOptions<MongoOptions> options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public string TargetDatabaseName => _options.DatabaseName;

    public ITrackingTransactionContext CreateContext(string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

        var database = _client.GetDatabase(databaseName);
        var companies = new MongoCompanyRepository(_client, database);
        var postings = new MongoPostingRepository(_client, database);
        var criteria = new MongoCriterionRepository(_client, database);
        var applications = new MongoApplicationRepository(_client, database);
        var contacts = new MongoContactRepository(_client, database);
        var searchRuns = new MongoSearchRunRepository(_client, database);
        var auditCol = database.GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        var notesCol = database.GetCollection<BsonDocument>("notes");

        return new MongoTrackingTransactionContext(
            _client,
            companies,
            postings,
            criteria,
            applications,
            contacts,
            searchRuns,
            auditCol,
            notesCol);
    }
}
