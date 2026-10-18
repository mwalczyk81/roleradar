using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Mongo;

namespace RoleRadar.Cli.Commands;

public static class StatusCommand
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static Command Create(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var statusCommand = new Command("status", "Check RoleRadar backing data store readiness and status");

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit structured JSON status output."
        };

        statusCommand.Add(jsonOption);

        statusCommand.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var json = parseResult.GetValue(jsonOption);

            using var scope = services.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<INoteRepository>();

            var readinessResult = await repository.CheckReadinessAsync(ct);

            if (readinessResult.IsSuccess && readinessResult.Value is not null)
            {
                var status = readinessResult.Value;

                var client = scope.ServiceProvider.GetService<IMongoClient>();
                var mongoOpts = scope.ServiceProvider.GetService<IOptions<MongoOptions>>();
                long companiesCount = 0;
                long postingsCount = 0;
                long criteriaCount = 0;
                long applicationsCount = 0;
                long signalsCount = 0;
                long searchRunsCount = 0;
                long contactsCount = 0;

                if (client is not null && mongoOpts is not null)
                {
                    var db = client.GetDatabase(mongoOpts.Value.DatabaseName);
                    companiesCount = await db.GetCollection<BsonDocument>(TrackingMongoOptions.CompaniesCollectionName).CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty, cancellationToken: ct);
                    postingsCount = await db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingsCollectionName).CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty, cancellationToken: ct);
                    criteriaCount = await db.GetCollection<BsonDocument>(TrackingMongoOptions.CriteriaCollectionName).CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty, cancellationToken: ct);
                    applicationsCount = await db.GetCollection<BsonDocument>(TrackingMongoOptions.ApplicationsCollectionName).CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty, cancellationToken: ct);
                    signalsCount = await db.GetCollection<BsonDocument>(TrackingMongoOptions.PostingSignalsCollectionName).CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty, cancellationToken: ct);
                    searchRunsCount = await db.GetCollection<BsonDocument>(TrackingMongoOptions.SearchRunsCollectionName).CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty, cancellationToken: ct);
                    contactsCount = await db.GetCollection<BsonDocument>(TrackingMongoOptions.ContactsCollectionName).CountDocumentsAsync(Builders<BsonDocument>.Filter.Empty, cancellationToken: ct);
                }

                if (json)
                {
                    var data = new
                    {
                        isReady = status.IsReady,
                        endpoint = status.Endpoint,
                        replicaSet = status.ReplicaSet,
                        latencyMs = status.LatencyMs,
                        companiesCount,
                        postingsCount,
                        criteriaCount,
                        applicationsCount,
                        signalsCount,
                        postingSignalsCount = signalsCount,
                        searchRunsCount,
                        contactsCount
                    };

                    var root = new
                    {
                        isReady = status.IsReady,
                        ok = true,
                        data
                    };

                    Console.WriteLine(JsonSerializer.Serialize(root, JsonOpts));
                    return 0;
                }

                Console.WriteLine("RoleRadar Store: Ready");
                Console.WriteLine($"Endpoint:        {status.Endpoint}");
                Console.WriteLine($"Replica Set:     {status.ReplicaSet}");
                Console.WriteLine($"Latency:         {status.LatencyMs:F2} ms");
                Console.WriteLine($"Companies:       {companiesCount}");
                Console.WriteLine($"Postings:        {postingsCount}");
                Console.WriteLine($"Criteria:        {criteriaCount}");
                Console.WriteLine($"Applications:    {applicationsCount}");
                Console.WriteLine($"Signals:         {signalsCount}");
                Console.WriteLine($"Search Runs:     {searchRunsCount}");
                Console.WriteLine($"Contacts:        {contactsCount}");
                return 0;
            }

            var error = readinessResult.Error!.Value;
            if (json)
            {
                var errorObj = new
                {
                    isReady = false,
                    ok = false,
                    code = error.Code.ToString(),
                    message = error.Message,
                    metadata = error.Metadata
                };
                Console.WriteLine(JsonSerializer.Serialize(errorObj, JsonOpts));
                return 1;
            }

            Console.Error.WriteLine(error.Message);
            return 1;
        });

        return statusCommand;
    }
}
