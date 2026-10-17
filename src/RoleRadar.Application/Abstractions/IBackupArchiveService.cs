namespace RoleRadar.Application.Abstractions;

public sealed record BackupArchiveSummaryDto(
    string ArchivePath,
    long BytesWritten,
    DateTime CreatedAtUtc);

public sealed record RestoreArchiveSummaryDto(
    string ArchivePath,
    IReadOnlyDictionary<string, long> RestoredCounts,
    DateTime CompletedAtUtc);

public sealed record WipeSummaryDto(
    IReadOnlyDictionary<string, long> DroppedCounts,
    DateTime CompletedAtUtc);

public interface IBackupArchiveService
{
    Task<Result<BackupArchiveSummaryDto>> CreateBackupAsync(
        string targetFilePath,
        string passphrase,
        CancellationToken cancellationToken = default);

    Task<Result<RestoreArchiveSummaryDto>> RestoreBackupAsync(
        string backupFilePath,
        string passphrase,
        bool forceOverwrite = false,
        CancellationToken cancellationToken = default);

    Task<Result<WipeSummaryDto>> WipeDatabaseAsync(
        string confirmation,
        CancellationToken cancellationToken = default);
}
