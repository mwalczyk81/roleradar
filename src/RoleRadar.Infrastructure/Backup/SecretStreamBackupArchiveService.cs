using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Mongo;

namespace RoleRadar.Infrastructure.Backup;

public sealed class SecretStreamBackupArchiveService : IBackupArchiveService
{
    private readonly IMongoClient _mongoClient;
    private readonly MongoOptions _mongoOptions;
    private readonly BackupOptions _backupOptions;
    private readonly ILogger<SecretStreamBackupArchiveService> _logger;

    public SecretStreamBackupArchiveService(
        IMongoClient mongoClient,
        IOptions<MongoOptions> mongoOptions,
        IOptions<BackupOptions>? backupOptions = null,
        ILogger<SecretStreamBackupArchiveService>? logger = null)
    {
        _mongoClient = mongoClient ?? throw new ArgumentNullException(nameof(mongoClient));
        _mongoOptions = mongoOptions?.Value ?? throw new ArgumentNullException(nameof(mongoOptions));
        _backupOptions = backupOptions?.Value ?? new BackupOptions();
        _logger = logger ?? NullLogger<SecretStreamBackupArchiveService>.Instance;
    }

    public Task<Result<long>> EncryptStreamAsync(
        Stream plaintextStream,
        Stream ciphertextStream,
        string passphrase,
        int iterations = BackupArchiveConstants.DefaultArgon2Iterations,
        int memoryKiB = BackupArchiveConstants.DefaultArgon2MemoryKiB,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<long>.Failure(ErrorCode.ValidationError, "Not implemented yet."));
    }

    public Task<Result<byte[]>> Pass1VerifyAsync(
        Stream ciphertextStream,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<byte[]>.Failure(ErrorCode.AuthenticationError, "Not implemented yet."));
    }

    public Task<Result<long>> Pass2DecryptAsync(
        Stream ciphertextStream,
        Stream plaintextStream,
        byte[] derivedKey,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<long>.Failure(ErrorCode.AuthenticationError, "Not implemented yet."));
    }

    public Task<Result<long>> DecryptStreamAsync(
        Stream ciphertextStream,
        Stream plaintextStream,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<long>.Failure(ErrorCode.AuthenticationError, "Not implemented yet."));
    }

    public Task<Result<BackupArchiveSummaryDto>> CreateBackupAsync(
        string targetFilePath,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<BackupArchiveSummaryDto>.Failure(ErrorCode.ValidationError, "Not implemented yet."));
    }

    public Task<Result<RestoreArchiveSummaryDto>> RestoreBackupAsync(
        string backupFilePath,
        string passphrase,
        bool forceOverwrite = false,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<RestoreArchiveSummaryDto>.Failure(ErrorCode.ValidationError, "Not implemented yet."));
    }

    public Task<Result<WipeSummaryDto>> WipeDatabaseAsync(
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<WipeSummaryDto>.Failure(ErrorCode.ValidationError, "Not implemented yet."));
    }
}
