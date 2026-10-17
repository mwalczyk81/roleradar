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
            var settings = MongoClientSettings.FromConnectionString(opts.ConnectionString);
            settings.ServerSelectionTimeout = opts.ReadinessTimeout;
            settings.ConnectTimeout = opts.ReadinessTimeout;
            return new MongoClient(settings);
        });

        services.TryAddSingleton<MongoReadinessProbe>();
        services.TryAddScoped<INoteRepository>(sp =>
            ActivatorUtilities.CreateInstance<MongoNoteRepository>(sp));
        services.AddOptions<RoleRadar.Infrastructure.Backup.BackupOptions>();
        services.TryAddScoped<IBackupArchiveService, RoleRadar.Infrastructure.Backup.SecretStreamBackupArchiveService>();

        return services;
    }
}
