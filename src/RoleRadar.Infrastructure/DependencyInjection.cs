using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Mongo;

namespace RoleRadar.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRoleRadarInfrastructure(
        this IServiceCollection services,
        Action<MongoOptions>? configureMongo = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        MongoBootstrapAndIndexes.RegisterSerializers();

        var optionsBuilder = services.AddOptions<MongoOptions>();
        if (configureMongo is not null)
        {
            optionsBuilder.Configure(configureMongo);
        }

        services.TryAddSingleton<IMongoClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            return ConnectionStringRedactor.CreateClient(opts.ConnectionString, opts.ReadinessTimeout);
        });

        services.TryAddSingleton<MongoReadinessProbe>();
        services.TryAddScoped<INoteRepository>(sp =>
            ActivatorUtilities.CreateInstance<MongoNoteRepository>(sp));
        services.TryAddScoped<ICompanyRepository>(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            var opts = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            var database = client.GetDatabase(opts.DatabaseName);
            return new RoleRadar.Infrastructure.Mongo.Repositories.MongoCompanyRepository(client, database);
        });
        services.TryAddScoped<IPostingRepository>(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            var opts = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            var database = client.GetDatabase(opts.DatabaseName);
            return new RoleRadar.Infrastructure.Mongo.Repositories.MongoPostingRepository(client, database);
        });
        services.TryAddScoped<ICriterionRepository>(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            var opts = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            var database = client.GetDatabase(opts.DatabaseName);
            return new RoleRadar.Infrastructure.Mongo.Repositories.MongoCriterionRepository(client, database);
        });
        services.TryAddScoped<ITrackingTransactionContext>(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            var opts = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            var database = client.GetDatabase(opts.DatabaseName);
            var companies = sp.GetRequiredService<ICompanyRepository>();
            var postings = sp.GetRequiredService<IPostingRepository>();
            var criteria = sp.GetRequiredService<ICriterionRepository>();
            var auditLog = database.GetCollection<MongoDB.Bson.BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
            return new RoleRadar.Infrastructure.Mongo.Repositories.MongoTrackingTransactionContext(companies, postings, criteria, auditLog);
        });
        services.AddOptions<RoleRadar.Infrastructure.Backup.BackupOptions>();
        services.TryAddScoped<IBackupArchiveService, RoleRadar.Infrastructure.Backup.SecretStreamBackupArchiveService>();

        return services;
    }
}
