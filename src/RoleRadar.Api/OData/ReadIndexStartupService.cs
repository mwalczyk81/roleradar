namespace RoleRadar.Api.OData;

using RoleRadar.Infrastructure.Mongo.Reading;

/// <summary>
/// Creates the tracking and notes indexes (nulls-last compounds, collation sort indexes) once, before the host
/// starts accepting requests. Without it an API that only serves OData reads has none of them until a repository
/// writes in this process. Idempotent and gated per database by the infrastructure <see cref="ReadIndexInitializer"/>. A failure
/// is logged and never stops the host: reads stay correct, just unindexed, and the read seam retries on the first OData read after Mongo is back.
/// </summary>
public sealed partial class ReadIndexStartupService : IHostedService
{
    private readonly ReadIndexInitializer _initializer;
    private readonly ILogger<ReadIndexStartupService> _logger;

    public ReadIndexStartupService(ReadIndexInitializer initializer, ILogger<ReadIndexStartupService> logger)
    {
        _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _initializer.EnsureAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogIndexCreationFailed(ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not create the read indexes at startup; OData reads continue without them.")]
    private partial void LogIndexCreationFailed(Exception exception);
}
