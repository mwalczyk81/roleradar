using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Geralt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Infrastructure.Mongo;
using ChunkFlag = Geralt.IncrementalXChaCha20Poly1305.ChunkFlag;

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

    public async Task<Result<long>> EncryptStreamAsync(
        Stream plaintextStream,
        Stream ciphertextStream,
        string passphrase,
        int iterations = BackupArchiveConstants.DefaultArgon2Iterations,
        int memoryKiB = BackupArchiveConstants.DefaultArgon2MemoryKiB,
        Func<CancellationToken, Task<Result<bool>>>? beforeFinalChunkAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plaintextStream);
        ArgumentNullException.ThrowIfNull(ciphertextStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        if (iterations < BackupArchiveConstants.MinArgon2Iterations || iterations > BackupArchiveConstants.MaxArgon2Iterations)
        {
            return Result<long>.Failure(ErrorCode.AuthenticationError, "Archive parameters are invalid.");
        }

        if (memoryKiB < BackupArchiveConstants.MinArgon2MemoryKiB || memoryKiB > BackupArchiveConstants.MaxArgon2MemoryKiB)
        {
            return Result<long>.Failure(ErrorCode.AuthenticationError, "Archive parameters are invalid.");
        }

        var salt = new byte[BackupArchiveConstants.SaltSize];
        RandomNumberGenerator.Fill(salt);

        var key = new byte[BackupArchiveConstants.KeySize];
        var passphraseBytes = Encoding.UTF8.GetBytes(passphrase);

        byte[] currentChunk = ArrayPool<byte>.Shared.Rent(BackupArchiveConstants.ChunkPlaintextSize);
        byte[] nextChunk = ArrayPool<byte>.Shared.Rent(BackupArchiveConstants.ChunkPlaintextSize);
        byte[] ciphertextBuffer = ArrayPool<byte>.Shared.Rent(BackupArchiveConstants.ChunkPlaintextSize + BackupArchiveConstants.TagSize);

        try
        {
            Geralt.Argon2id.DeriveKey(key, passphraseBytes, salt, iterations, memoryKiB * 1024);

            var streamHeader = new byte[BackupArchiveConstants.StreamHeaderSize];
            using var streamEncryptor = new IncrementalXChaCha20Poly1305(streamHeader, key, encryption: true);

            var fileHeader = new byte[BackupArchiveConstants.HeaderSize];
            Array.Copy(BackupArchiveConstants.Magic, 0, fileHeader, 0, 4);
            fileHeader[4] = BackupArchiveConstants.CurrentFormatVersion;
            fileHeader[5] = BackupArchiveConstants.ExpectedFlags;
            Array.Copy(BitConverter.GetBytes(iterations), 0, fileHeader, 6, 4);
            Array.Copy(BitConverter.GetBytes(memoryKiB), 0, fileHeader, 10, 4);
            Array.Copy(salt, 0, fileHeader, 14, BackupArchiveConstants.SaltSize);
            Array.Copy(streamHeader, 0, fileHeader, 30, BackupArchiveConstants.StreamHeaderSize);

            await ciphertextStream.WriteAsync(fileHeader, cancellationToken);

            long totalPlaintextBytes = 0;
            var hasCurrentChunk = false;
            var currentChunkLen = 0;
            var isFirstChunk = true;
            var chunkLengthBytes = new byte[4];

            while (true)
            {
                var bytesRead = await ReadExactOrToEofAsync(plaintextStream, nextChunk, 0, BackupArchiveConstants.ChunkPlaintextSize, cancellationToken);

                if (!hasCurrentChunk)
                {
                    if (bytesRead == 0)
                    {
                        // 0-byte input stream
                        if (beforeFinalChunkAsync is not null)
                        {
                            var gate = await beforeFinalChunkAsync(cancellationToken);
                            if (!gate.IsSuccess)
                            {
                                return Result<long>.Failure(gate.Error ?? new ResultError(ErrorCode.ValidationError, "Final chunk holdback check failed."));
                            }
                        }

                        streamEncryptor.EncryptChunk(
                            ciphertextBuffer.AsSpan(0, BackupArchiveConstants.TagSize),
                            ReadOnlySpan<byte>.Empty,
                            fileHeader,
                            ChunkFlag.Final);

                        BitConverter.TryWriteBytes(chunkLengthBytes, BackupArchiveConstants.TagSize);
                        await ciphertextStream.WriteAsync(chunkLengthBytes, cancellationToken);
                        await ciphertextStream.WriteAsync(ciphertextBuffer.AsMemory(0, BackupArchiveConstants.TagSize), cancellationToken);
                        break;
                    }

                    Array.Copy(nextChunk, currentChunk, bytesRead);
                    currentChunkLen = bytesRead;
                    hasCurrentChunk = true;
                }
                else
                {
                    var isTerminal = bytesRead == 0;
                    if (isTerminal && beforeFinalChunkAsync is not null)
                    {
                        var gate = await beforeFinalChunkAsync(cancellationToken);
                        if (!gate.IsSuccess)
                        {
                            return Result<long>.Failure(gate.Error ?? new ResultError(ErrorCode.ValidationError, "Final chunk holdback check failed."));
                        }
                    }

                    var flag = isTerminal ? ChunkFlag.Final : ChunkFlag.Message;
                    var ad = isFirstChunk ? fileHeader : ReadOnlySpan<byte>.Empty;
                    var cipherLen = currentChunkLen + BackupArchiveConstants.TagSize;

                    streamEncryptor.EncryptChunk(
                        ciphertextBuffer.AsSpan(0, cipherLen),
                        currentChunk.AsSpan(0, currentChunkLen),
                        ad,
                        flag);

                    BitConverter.TryWriteBytes(chunkLengthBytes, cipherLen);
                    await ciphertextStream.WriteAsync(chunkLengthBytes, cancellationToken);
                    await ciphertextStream.WriteAsync(ciphertextBuffer.AsMemory(0, cipherLen), cancellationToken);

                    totalPlaintextBytes += currentChunkLen;
                    isFirstChunk = false;

                    if (isTerminal)
                    {
                        break;
                    }

                    Array.Copy(nextChunk, currentChunk, bytesRead);
                    currentChunkLen = bytesRead;
                }
            }

            return Result<long>.Success(totalPlaintextBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(currentChunk, clearArray: true);
            ArrayPool<byte>.Shared.Return(nextChunk, clearArray: true);
            ArrayPool<byte>.Shared.Return(ciphertextBuffer, clearArray: true);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(passphraseBytes);
        }
    }

    public async Task<Result<byte[]>> Pass1VerifyAsync(
        Stream ciphertextStream,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        var result = await Pass1VerifyWithMetadataAsync(ciphertextStream, passphrase, cancellationToken);
        if (!result.IsSuccess || result.Value.DerivedKey is null)
        {
            return Result<byte[]>.Failure(result.Error ?? new ResultError(ErrorCode.AuthenticationError, "Archive is invalid or corrupted."));
        }

        return Result<byte[]>.Success(result.Value.DerivedKey);
    }

    internal async Task<Result<(byte[] DerivedKey, string? SourceDatabaseName)>> Pass1VerifyWithMetadataAsync(
        Stream ciphertextStream,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ciphertextStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        var header = new byte[BackupArchiveConstants.HeaderSize];
        var readHeader = await ReadExactOrToEofAsync(ciphertextStream, header, 0, BackupArchiveConstants.HeaderSize, cancellationToken);
        if (readHeader < BackupArchiveConstants.HeaderSize)
        {
            return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
        }

        // Strict pre-KDF bounds and format verification
        if (!header.AsSpan(0, 4).SequenceEqual(BackupArchiveConstants.Magic))
        {
            return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
        }

        if (header[4] != BackupArchiveConstants.CurrentFormatVersion || header[5] != BackupArchiveConstants.ExpectedFlags)
        {
            return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
        }

        var iterations = BitConverter.ToInt32(header, 6);
        if (iterations < BackupArchiveConstants.MinArgon2Iterations || iterations > BackupArchiveConstants.MaxArgon2Iterations)
        {
            return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
        }

        var memoryKiB = BitConverter.ToInt32(header, 10);
        if (memoryKiB < BackupArchiveConstants.MinArgon2MemoryKiB || memoryKiB > BackupArchiveConstants.MaxArgon2MemoryKiB)
        {
            return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
        }

        var salt = header.AsSpan(14, BackupArchiveConstants.SaltSize);
        var streamHeader = header.AsSpan(30, BackupArchiveConstants.StreamHeaderSize);

        var derivedKey = new byte[BackupArchiveConstants.KeySize];
        var passphraseBytes = Encoding.UTF8.GetBytes(passphrase);

        byte[] chunkBuffer = ArrayPool<byte>.Shared.Rent(BackupArchiveConstants.MaxCiphertextLength);
        byte[] scratchPlaintext = ArrayPool<byte>.Shared.Rent(BackupArchiveConstants.MaxCiphertextLength);

        try
        {
            Geralt.Argon2id.DeriveKey(derivedKey, passphraseBytes, salt, iterations, memoryKiB * 1024);

            var streamHeaderCopy = streamHeader.ToArray();
            using var decryptor = new IncrementalXChaCha20Poly1305(streamHeaderCopy, derivedKey, encryption: false);

            var lengthBuffer = new byte[4];
            var isFirstChunk = true;
            var observedFinal = false;
            string? extractedSourceDb = null;

            while (true)
            {
                var lenRead = await ReadExactOrToEofAsync(ciphertextStream, lengthBuffer, 0, 4, cancellationToken);
                if (lenRead == 0)
                {
                    break;
                }

                if (lenRead < 4)
                {
                    return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
                }

                var chunkLen = BitConverter.ToInt32(lengthBuffer, 0);
                if (chunkLen < BackupArchiveConstants.MinCiphertextLength || chunkLen > BackupArchiveConstants.MaxCiphertextLength)
                {
                    return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
                }

                var chunkRead = await ReadExactOrToEofAsync(ciphertextStream, chunkBuffer, 0, chunkLen, cancellationToken);
                if (chunkRead < chunkLen)
                {
                    return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
                }

                var ad = isFirstChunk ? header.AsSpan() : ReadOnlySpan<byte>.Empty;
                var plainLen = chunkLen - BackupArchiveConstants.TagSize;

                ChunkFlag flag;
                try
                {
                    flag = decryptor.DecryptChunk(
                        scratchPlaintext.AsSpan(0, plainLen),
                        chunkBuffer.AsSpan(0, chunkLen),
                        ad);
                }
                catch (Exception)
                {
                    return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
                }

                if (isFirstChunk)
                {
                    TryExtractSourceDatabaseName(scratchPlaintext.AsSpan(0, plainLen), out extractedSourceDb);
                }

                isFirstChunk = false;

                if (flag == ChunkFlag.Final)
                {
                    observedFinal = true;
                    // Check for trailing bytes after Final chunk
                    var trailingTest = new byte[1];
                    var trailingRead = await ciphertextStream.ReadAsync(trailingTest, cancellationToken);
                    if (trailingRead > 0)
                    {
                        return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
                    }
                    break;
                }
            }

            if (!observedFinal)
            {
                return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
            }

            // Return derived key so Pass 2 can reuse it without recomputing Argon2
            var keyCopy = (byte[])derivedKey.Clone();
            return Result<(byte[], string?)>.Success((keyCopy, extractedSourceDb));
        }
        catch (Exception)
        {
            return Result<(byte[], string?)>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunkBuffer, clearArray: true);
            ArrayPool<byte>.Shared.Return(scratchPlaintext, clearArray: true);
            CryptographicOperations.ZeroMemory(derivedKey);
            CryptographicOperations.ZeroMemory(passphraseBytes);
        }
    }

    public async Task<Result<long>> Pass2DecryptAsync(
        Stream ciphertextStream,
        Stream plaintextStream,
        byte[] derivedKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ciphertextStream);
        ArgumentNullException.ThrowIfNull(plaintextStream);
        ArgumentNullException.ThrowIfNull(derivedKey);

        ciphertextStream.Position = 0;

        var header = new byte[BackupArchiveConstants.HeaderSize];
        var readHeader = await ReadExactOrToEofAsync(ciphertextStream, header, 0, BackupArchiveConstants.HeaderSize, cancellationToken);
        if (readHeader < BackupArchiveConstants.HeaderSize)
        {
            return Result<long>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
        }

        var streamHeader = header.AsSpan(30, BackupArchiveConstants.StreamHeaderSize).ToArray();
        using var decryptor = new IncrementalXChaCha20Poly1305(streamHeader, derivedKey, encryption: false);

        var lengthBuffer = new byte[4];
        byte[] chunkBuffer = ArrayPool<byte>.Shared.Rent(BackupArchiveConstants.MaxCiphertextLength);
        byte[] plaintextBuffer = ArrayPool<byte>.Shared.Rent(BackupArchiveConstants.MaxCiphertextLength);

        try
        {
            long totalDecrypted = 0;
            var isFirstChunk = true;

            while (true)
            {
                var lenRead = await ReadExactOrToEofAsync(ciphertextStream, lengthBuffer, 0, 4, cancellationToken);
                if (lenRead == 0)
                {
                    break;
                }

                var chunkLen = BitConverter.ToInt32(lengthBuffer, 0);
                if (chunkLen < BackupArchiveConstants.MinCiphertextLength || chunkLen > BackupArchiveConstants.MaxCiphertextLength)
                {
                    return Result<long>.Failure(ErrorCode.AuthenticationError, "Archive is invalid or corrupted.");
                }

                await ReadExactOrToEofAsync(ciphertextStream, chunkBuffer, 0, chunkLen, cancellationToken);

                var ad = isFirstChunk ? header.AsSpan() : ReadOnlySpan<byte>.Empty;
                var plainLen = chunkLen - BackupArchiveConstants.TagSize;

                decryptor.DecryptChunk(
                    plaintextBuffer.AsSpan(0, plainLen),
                    chunkBuffer.AsSpan(0, chunkLen),
                    ad);

                if (plainLen > 0)
                {
                    await plaintextStream.WriteAsync(plaintextBuffer.AsMemory(0, plainLen), cancellationToken);
                    totalDecrypted += plainLen;
                }

                isFirstChunk = false;
            }

            return Result<long>.Success(totalDecrypted);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunkBuffer, clearArray: true);
            ArrayPool<byte>.Shared.Return(plaintextBuffer, clearArray: true);
        }
    }

    public async Task<Result<long>> DecryptStreamAsync(
        Stream ciphertextStream,
        Stream plaintextStream,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        // Two-Pass Decrypt: Pass 1 verifies and derives key, Pass 2 applies using identical derived key
        var pass1 = await Pass1VerifyAsync(ciphertextStream, passphrase, cancellationToken);
        if (!pass1.IsSuccess || pass1.Value is null)
        {
            return Result<long>.Failure(pass1.Error ?? new ResultError(ErrorCode.AuthenticationError, "Authentication failed."));
        }

        var key = pass1.Value;
        try
        {
            return await Pass2DecryptAsync(ciphertextStream, plaintextStream, key, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task<Result<BackupArchiveSummaryDto>> CreateBackupAsync(
        string targetFilePath,
        string passphrase,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        var resolvedTarget = ResolveTargetBackupFilePath(targetFilePath);
        if (File.Exists(resolvedTarget))
        {
            return Result<BackupArchiveSummaryDto>.Failure(
                ErrorCode.ValidationError,
                $"Backup file '{resolvedTarget}' already exists.");
        }

        var targetDir = Path.GetDirectoryName(resolvedTarget);
        if (!string.IsNullOrEmpty(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        var partialPath = resolvedTarget + ".partial";
        if (File.Exists(partialPath))
        {
            File.Delete(partialPath);
        }

        Process? process = null;
        string? tempConfigFile = null;
        Task<string>? stderrDrainTask = null;

        EventHandler exitHandler = (_, _) =>
        {
            try { if (process is not null && !process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { if (tempConfigFile is not null && File.Exists(tempConfigFile)) File.Delete(tempConfigFile); } catch { }
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
        };
        AppDomain.CurrentDomain.ProcessExit += exitHandler;

        PosixSignalRegistration? sigtermReg = null;
        PosixSignalRegistration? sigintReg = null;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            Action<PosixSignalContext> signalHandler = _ =>
            {
                try { if (process is not null && !process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                try { if (tempConfigFile is not null && File.Exists(tempConfigFile)) File.Delete(tempConfigFile); } catch { }
                try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
            };
            sigtermReg = PosixSignalRegistration.Create(PosixSignal.SIGTERM, signalHandler);
            sigintReg = PosixSignalRegistration.Create(PosixSignal.SIGINT, signalHandler);
        }

        try
        {
            var isMongoTools = string.Equals(_backupOptions.DumpCommand, "mongodump", StringComparison.OrdinalIgnoreCase) ||
                               _backupOptions.DumpCommand.StartsWith("mongodump ", StringComparison.OrdinalIgnoreCase) ||
                               File.Exists("/usr/local/bin/mongodump");

            string cmdExe;
            string cmdArgs;

            if (isMongoTools)
            {
                tempConfigFile = CreateAtomicToolsConfigFile(_mongoOptions.ConnectionString);
                cmdExe = "mongodump";
                cmdArgs = $"--config=\"{tempConfigFile}\" --db=\"{_mongoOptions.DatabaseName}\" --archive --gzip";
            }
            else
            {
                (cmdExe, cmdArgs) = SplitCommand(_backupOptions.DumpCommand);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = cmdExe,
                Arguments = cmdArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            SanitizeChildEnvironment(startInfo);

            try
            {
                process = Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                return Result<BackupArchiveSummaryDto>.Failure(
                    ErrorCode.ValidationError,
                    $"Failed to start mongodump command '{cmdExe}'. Ensure tools are available: {ConnectionStringRedactor.RedactSensitiveText(ex.Message, _mongoOptions.ConnectionString)}");
            }

            if (process is null)
            {
                return Result<BackupArchiveSummaryDto>.Failure(
                    ErrorCode.ValidationError,
                    "Failed to start mongodump process.");
            }

            stderrDrainTask = Task.Run(async () =>
            {
                using var reader = new StreamReader(process.StandardError.BaseStream, Encoding.UTF8);
                return await reader.ReadToEndAsync(cancellationToken);
            }, cancellationToken);

            var dumpStdout = process.StandardOutput.BaseStream;
            var firstChunk = new byte[BackupArchiveConstants.ChunkPlaintextSize];
            var firstRead = await ReadExactOrToEofAsync(dumpStdout, firstChunk, 0, BackupArchiveConstants.ChunkPlaintextSize, cancellationToken);

            if (firstRead == 0)
            {
                await TerminateProcessTreeAsync(process);
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                }

                var rawStderr = await ReadStderrAsync(stderrDrainTask);
                var stderr = ConnectionStringRedactor.RedactSensitiveText(rawStderr, _mongoOptions.ConnectionString);
                return Result<BackupArchiveSummaryDto>.Failure(
                    ErrorCode.ValidationError,
                    $"mongodump produced zero bytes. An empty archive cannot be created: {stderr}");
            }

            if (!IsValidMongodumpArchive(firstChunk, firstRead))
            {
                await TerminateProcessTreeAsync(process);
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                }

                var rawStderr = await ReadStderrAsync(stderrDrainTask);
                var stderr = ConnectionStringRedactor.RedactSensitiveText(rawStderr, _mongoOptions.ConnectionString);
                return Result<BackupArchiveSummaryDto>.Failure(
                    ErrorCode.ValidationError,
                    $"mongodump stream is not a valid archive. Decoded stream does not match mongodump archive magic: {stderr}");
            }

            using var prefixStream = new PrefixStream(firstChunk, firstRead, dumpStdout);

            long bytesWritten = 0;
            await using (var outputFile = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var encResult = await EncryptStreamAsync(
                    prefixStream,
                    outputFile,
                    passphrase,
                    beforeFinalChunkAsync: async ct =>
                    {
                        await process.WaitForExitAsync(ct);
                        if (process.ExitCode != 0)
                        {
                            var rawStderr = await ReadStderrAsync(stderrDrainTask);
                            var stderr = ConnectionStringRedactor.RedactSensitiveText(rawStderr, _mongoOptions.ConnectionString);
                            return Result<bool>.Failure(
                                ErrorCode.ValidationError,
                                $"mongodump failed with exit code {process.ExitCode}: {stderr}");
                        }
                        return Result<bool>.Success(true);
                    },
                    cancellationToken: cancellationToken);

                if (!encResult.IsSuccess)
                {
                    await TerminateProcessTreeAsync(process);
                    if (File.Exists(partialPath))
                    {
                        File.Delete(partialPath);
                    }

                    return Result<BackupArchiveSummaryDto>.Failure(encResult.Error!.Value);
                }

                await outputFile.FlushAsync(cancellationToken);
                bytesWritten = outputFile.Length;
            }

            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
            {
                var rawStderr = await ReadStderrAsync(stderrDrainTask);
                var stderr = ConnectionStringRedactor.RedactSensitiveText(rawStderr, _mongoOptions.ConnectionString);
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                }

                return Result<BackupArchiveSummaryDto>.Failure(
                    ErrorCode.ValidationError,
                    $"mongodump failed with exit code {process.ExitCode}: {stderr}");
            }

            // Atomic rename
            File.Move(partialPath, resolvedTarget);

            return Result<BackupArchiveSummaryDto>.Success(
                new BackupArchiveSummaryDto(resolvedTarget, bytesWritten, DateTime.UtcNow));
        }
        catch (Exception)
        {
            await TerminateProcessTreeAsync(process);
            if (File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch { }
            }
            throw;
        }
        finally
        {
            AppDomain.CurrentDomain.ProcessExit -= exitHandler;
            sigtermReg?.Dispose();
            sigintReg?.Dispose();
            await TerminateProcessTreeAsync(process);
            if (tempConfigFile is not null && File.Exists(tempConfigFile))
            {
                try { File.Delete(tempConfigFile); } catch { }
            }
            if (File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch { }
            }
            process?.Dispose();
        }
    }

    public async Task<Result<RestoreArchiveSummaryDto>> RestoreBackupAsync(
        string backupFilePath,
        string passphrase,
        bool forceOverwrite = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        // Check if database contains data across all 12 collections
        var db = _mongoClient.GetDatabase(_mongoOptions.DatabaseName);

        var allCollectionNames = new[]
        {
            MongoBootstrapAndIndexes.NotesCollectionName,
            MongoBootstrapAndIndexes.NoteRevisionsCollectionName,
            MongoBootstrapAndIndexes.AuditLogCollectionName,
            TrackingMongoOptions.CompaniesCollectionName,
            TrackingMongoOptions.CareerSourcesCollectionName,
            TrackingMongoOptions.PostingsCollectionName,
            TrackingMongoOptions.PostingSignalsCollectionName,
            TrackingMongoOptions.CriteriaCollectionName,
            TrackingMongoOptions.ApplicationsCollectionName,
            TrackingMongoOptions.ApplicationEventsCollectionName,
            TrackingMongoOptions.ContactsCollectionName,
            TrackingMongoOptions.SearchRunsCollectionName
        };

        if (!forceOverwrite)
        {
            foreach (var colName in allCollectionNames)
            {
                var col = db.GetCollection<BsonDocument>(colName);
                var count = await col.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: cancellationToken);
                if (count > 0)
                {
                    return Result<RestoreArchiveSummaryDto>.Failure(
                        ErrorCode.ValidationError,
                        "Datastore is non-empty. Use --force-overwrite to drop existing collections before restoring.");
                }
            }
        }

        if (!File.Exists(backupFilePath))
        {
            return Result<RestoreArchiveSummaryDto>.Failure(
                ErrorCode.NotFound,
                $"Backup archive '{backupFilePath}' does not exist.");
        }

        // Single FileStream opened for both Pass 1 and Pass 2
        await using var fileStream = new FileStream(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);

        var pass1Result = await Pass1VerifyWithMetadataAsync(fileStream, passphrase, cancellationToken);
        if (!pass1Result.IsSuccess || pass1Result.Value.DerivedKey is null)
        {
            return Result<RestoreArchiveSummaryDto>.Failure(
                pass1Result.Error ?? new ResultError(ErrorCode.AuthenticationError, "Archive is invalid or corrupted."));
        }
        var (derivedKey, sourceDb) = pass1Result.Value;

        // Pass 2: Stream decrypted bytes into mongorestore stdin
        Process? process = null;
        string? tempConfigFile = null;

        EventHandler exitHandler = (_, _) =>
        {
            try { if (process is not null && !process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            try { if (tempConfigFile is not null && File.Exists(tempConfigFile)) File.Delete(tempConfigFile); } catch { }
        };
        AppDomain.CurrentDomain.ProcessExit += exitHandler;

        PosixSignalRegistration? sigtermReg = null;
        PosixSignalRegistration? sigintReg = null;
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            Action<PosixSignalContext> signalHandler = _ =>
            {
                try { if (process is not null && !process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                try { if (tempConfigFile is not null && File.Exists(tempConfigFile)) File.Delete(tempConfigFile); } catch { }
            };
            sigtermReg = PosixSignalRegistration.Create(PosixSignal.SIGTERM, signalHandler);
            sigintReg = PosixSignalRegistration.Create(PosixSignal.SIGINT, signalHandler);
        }

        try
        {
            var isMongoTools = string.Equals(_backupOptions.RestoreCommand, "mongorestore", StringComparison.OrdinalIgnoreCase) ||
                               _backupOptions.RestoreCommand.StartsWith("mongorestore ", StringComparison.OrdinalIgnoreCase) ||
                               File.Exists("/usr/local/bin/mongorestore");

            string cmdExe;
            string cmdArgs;

            var isRemapped = sourceDb is not null && !string.Equals(sourceDb, _mongoOptions.DatabaseName, StringComparison.Ordinal);

            if (isMongoTools)
            {
                tempConfigFile = CreateAtomicToolsConfigFile(_mongoOptions.ConnectionString);
                cmdExe = "mongorestore";
                var dropFlag = forceOverwrite ? "--drop " : string.Empty;
                var nsArgs = isRemapped
                    ? $"--nsFrom=\"{sourceDb}.*\" --nsTo=\"{_mongoOptions.DatabaseName}.*\""
                    : $"--nsInclude=\"{_mongoOptions.DatabaseName}.*\"";
                cmdArgs = $"--config=\"{tempConfigFile}\" {dropFlag}{nsArgs} --archive --gzip";
            }
            else
            {
                var restoreCmdString = _backupOptions.RestoreCommand;
                if (isRemapped)
                {
                    if (Regex.IsMatch(restoreCmdString, @"--nsInclude(=|\s+)""?[^""\s]+""?", RegexOptions.IgnoreCase))
                    {
                        restoreCmdString = Regex.Replace(
                            restoreCmdString,
                            @"--nsInclude(=|\s+)""?[^""\s]+""?",
                            $"--nsFrom=\"{sourceDb}.*\" --nsTo=\"{_mongoOptions.DatabaseName}.*\"",
                            RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        restoreCmdString += $" --nsFrom=\"{sourceDb}.*\" --nsTo=\"{_mongoOptions.DatabaseName}.*\"";
                    }
                }
                else
                {
                    if (restoreCmdString.Contains("<db>", StringComparison.OrdinalIgnoreCase))
                    {
                        restoreCmdString = restoreCmdString.Replace("<db>", _mongoOptions.DatabaseName);
                    }
                }

                if (restoreCmdString.Contains("<db>", StringComparison.OrdinalIgnoreCase))
                {
                    restoreCmdString = restoreCmdString.Replace("<db>", _mongoOptions.DatabaseName);
                }

                if ((restoreCmdString.Contains("--nsInclude", StringComparison.OrdinalIgnoreCase) ||
                     restoreCmdString.Contains("--nsFrom", StringComparison.OrdinalIgnoreCase)) &&
                    restoreCmdString.Contains("--oplogReplay", StringComparison.OrdinalIgnoreCase))
                {
                    // mongorestore fails if both --oplogReplay and namespace mapping options are present
                    restoreCmdString = Regex.Replace(restoreCmdString, @"\s*--oplogReplay\b", "");
                }

                if (forceOverwrite && !restoreCmdString.Contains("--drop", StringComparison.OrdinalIgnoreCase))
                {
                    restoreCmdString += " --drop";
                }

                (cmdExe, cmdArgs) = SplitCommand(restoreCmdString);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = cmdExe,
                Arguments = cmdArgs,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            SanitizeChildEnvironment(startInfo);

            try
            {
                process = Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                return Result<RestoreArchiveSummaryDto>.Failure(
                    ErrorCode.ValidationError,
                    $"Failed to start mongorestore command '{cmdExe}'. Ensure tools are available: {ConnectionStringRedactor.RedactSensitiveText(ex.Message, _mongoOptions.ConnectionString)}");
            }

            if (process is null)
            {
                return Result<RestoreArchiveSummaryDto>.Failure(
                    ErrorCode.ValidationError,
                    "Failed to start mongorestore process.");
            }

            var stderrTask = Task.Run(async () =>
            {
                using var reader = new StreamReader(process.StandardError.BaseStream, Encoding.UTF8);
                return await reader.ReadToEndAsync(cancellationToken);
            }, cancellationToken);

            // Rewind fileStream for Pass 2
            fileStream.Position = 0;
            var restoreStdin = process.StandardInput.BaseStream;
            var decryptResult = await Pass2DecryptAsync(fileStream, restoreStdin, derivedKey, cancellationToken);
            if (!decryptResult.IsSuccess)
            {
                await TerminateProcessTreeAsync(process);
                return Result<RestoreArchiveSummaryDto>.Failure(decryptResult.Error!.Value);
            }
            await restoreStdin.FlushAsync(cancellationToken);

            process.StandardInput.Close();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                var rawStderr = await ReadStderrAsync(stderrTask);
                var stderr = ConnectionStringRedactor.RedactSensitiveText(rawStderr, _mongoOptions.ConnectionString);
                return Result<RestoreArchiveSummaryDto>.Failure(
                    ErrorCode.ValidationError,
                    $"mongorestore failed with exit code {process.ExitCode}: {stderr}. The datastore may be partially restored; wipe and retry.");
            }

            // Verify indexes and collection counts across all 12 collections
            await MongoBootstrapAndIndexes.EnsureIndexesAsync(db, cancellationToken);
            try
            {
                await TrackingIndexInitializer.EnsureIndexesAsync(db, cancellationToken);
            }
            catch (TrackingIndexValidationException ex)
            {
                return Result<RestoreArchiveSummaryDto>.Failure(ErrorCode.ValidationError, ex.Message);
            }

            var counts = new Dictionary<string, long>();
            foreach (var colName in allCollectionNames)
            {
                var col = db.GetCollection<BsonDocument>(colName);
                counts[colName] = await col.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: cancellationToken);
            }

            var totalRestored = counts.Values.Sum();
            if (sourceDb is not null && totalRestored == 0)
            {
                var countsFormatted = string.Join(", ", counts.Select(kv => $"{kv.Value} {kv.Key}"));
                return Result<RestoreArchiveSummaryDto>.Failure(
                    ErrorCode.ValidationError,
                    $"Unmatched namespace or empty restore: archive contained documents from database '{sourceDb}' but zero documents were restored into '{_mongoOptions.DatabaseName}'. Document counts restored per collection: {countsFormatted}.");
            }

            return Result<RestoreArchiveSummaryDto>.Success(
                new RestoreArchiveSummaryDto(backupFilePath, counts, DateTime.UtcNow));
        }
        catch (Exception)
        {
            await TerminateProcessTreeAsync(process);
            throw;
        }
        finally
        {
            AppDomain.CurrentDomain.ProcessExit -= exitHandler;
            sigtermReg?.Dispose();
            sigintReg?.Dispose();
            await TerminateProcessTreeAsync(process);
            if (tempConfigFile is not null && File.Exists(tempConfigFile))
            {
                try { File.Delete(tempConfigFile); } catch { }
            }
            if (derivedKey is not null)
            {
                CryptographicOperations.ZeroMemory(derivedKey);
            }
            process?.Dispose();
        }
    }

    public async Task<Result<WipeSummaryDto>> WipeDatabaseAsync(
        string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (confirmation != "WIPE-ALL-ROLERADAR-DATA")
        {
            return Result<WipeSummaryDto>.Failure(
                ErrorCode.ValidationError,
                "Wipe requires explicit confirmation token '--confirm WIPE-ALL-ROLERADAR-DATA'.");
        }

        // Verify MongoDB connection string points to loopback
        try
        {
            var trimmed = _mongoOptions.ConnectionString.Trim();
            var mongoUrl = MongoUrl.Create(trimmed);
            if (trimmed.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
            {
                mongoUrl = mongoUrl.Resolve(resolveHosts: true, cancellationToken);
            }

            foreach (var server in mongoUrl.Servers)
            {
                var host = server.Host.Trim('[', ']');
                if (!host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) &&
                    !host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
                    !host.Equals("::1", StringComparison.OrdinalIgnoreCase) &&
                    !host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase))
                {
                    return Result<WipeSummaryDto>.Failure(
                        ErrorCode.ValidationError,
                        $"Wipe is only permitted against loopback MongoDB hosts (127.0.0.1, ::1, localhost, host.docker.internal). Found host: '{server.Host}'.");
                }
            }
        }
        catch (Exception ex) when (ex is MongoConfigurationException or FormatException or ArgumentException)
        {
            return Result<WipeSummaryDto>.Failure(
                ErrorCode.ValidationError,
                ConnectionStringRedactor.InvalidConnectionStringErrorMessage);
        }

        var db = _mongoClient.GetDatabase(_mongoOptions.DatabaseName);

        var allCollectionNames = new[]
        {
            MongoBootstrapAndIndexes.NotesCollectionName,
            MongoBootstrapAndIndexes.NoteRevisionsCollectionName,
            MongoBootstrapAndIndexes.AuditLogCollectionName,
            TrackingMongoOptions.CompaniesCollectionName,
            TrackingMongoOptions.CareerSourcesCollectionName,
            TrackingMongoOptions.PostingsCollectionName,
            TrackingMongoOptions.PostingSignalsCollectionName,
            TrackingMongoOptions.CriteriaCollectionName,
            TrackingMongoOptions.ApplicationsCollectionName,
            TrackingMongoOptions.ApplicationEventsCollectionName,
            TrackingMongoOptions.ContactsCollectionName,
            TrackingMongoOptions.SearchRunsCollectionName
        };

        var droppedCounts = new Dictionary<string, long>();
        foreach (var colName in allCollectionNames)
        {
            var col = db.GetCollection<BsonDocument>(colName);
            droppedCounts[colName] = await col.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty, cancellationToken: cancellationToken);
        }

        // Drop the database
        await _mongoClient.DropDatabaseAsync(_mongoOptions.DatabaseName, cancellationToken);

        // Re-create the indexes on empty collections
        await MongoBootstrapAndIndexes.EnsureIndexesAsync(db, cancellationToken);
        await TrackingIndexInitializer.EnsureIndexesAsync(db, cancellationToken);
        TrackingIndexInitializer.ResetInitializationState(_mongoOptions.DatabaseName);

        return Result<WipeSummaryDto>.Success(
            new WipeSummaryDto(droppedCounts, DateTime.UtcNow));
    }

    internal static bool TryExtractSourceDatabaseName(ReadOnlySpan<byte> chunk, out string? sourceDb)
    {
        sourceDb = null;
        if (chunk.Length < 4)
        {
            return false;
        }

        // 1. Check if chunk is gzip-compressed (0x1f, 0x8b)
        if (chunk.Length >= 2 && chunk[0] == 0x1f && chunk[1] == 0x8b)
        {
            try
            {
                using var ms = new MemoryStream(chunk.ToArray());
                using var gzip = new GZipStream(ms, CompressionMode.Decompress);
                var decompBuffer = new byte[65536];
                var totalRead = 0;
                while (totalRead < decompBuffer.Length)
                {
                    var read = gzip.Read(decompBuffer, totalRead, decompBuffer.Length - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }

                return TryScanDatabaseNameFromBson(decompBuffer.AsSpan(0, totalRead), out sourceDb);
            }
            catch
            {
                return false;
            }
        }

        // 2. Uncompressed stream
        return TryScanDatabaseNameFromBson(chunk, out sourceDb);
    }

    private static bool TryScanDatabaseNameFromBson(ReadOnlySpan<byte> span, out string? sourceDb)
    {
        sourceDb = null;
        if (span.Length < 8)
        {
            return false;
        }

        // In mongodump archive format, collection header BSON document contains element:
        // byte 0x02 (String), "db\0", 4-byte int32 string length (including \0), UTF-8 string, \0.
        ReadOnlySpan<byte> dbElementPattern = [0x02, (byte)'d', (byte)'b', 0x00];
        var idx = span.IndexOf(dbElementPattern);
        if (idx >= 0 && idx + 8 <= span.Length)
        {
            var strLen = BitConverter.ToInt32(span.Slice(idx + 4, 4));
            if (strLen > 1 && idx + 8 + strLen <= span.Length)
            {
                var nameSpan = span.Slice(idx + 8, strLen - 1);
                var candidate = Encoding.UTF8.GetString(nameSpan);
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    sourceDb = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsValidMongodumpArchive(byte[] chunk, int count)
    {
        if (count < 4)
        {
            return false;
        }

        var span = chunk.AsSpan(0, count);

        // 1. Direct uncompressed archive magic
        if (MatchesArchiveMagic(span))
        {
            return true;
        }

        // 2. GZIP compressed archive (0x1f, 0x8b)
        if (span.Length >= 2 && span[0] == 0x1f && span[1] == 0x8b)
        {
            try
            {
                using var ms = new MemoryStream(chunk, 0, count);
                using var gzip = new GZipStream(ms, CompressionMode.Decompress);
                var decompHeader = new byte[8];
                var read = gzip.Read(decompHeader, 0, decompHeader.Length);
                if (read >= 4 && MatchesArchiveMagic(decompHeader.AsSpan(0, read)))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static bool MatchesArchiveMagic(ReadOnlySpan<byte> span)
    {
        if (span.Length < 4)
        {
            return false;
        }

        ReadOnlySpan<byte> magic1 = [0x6d, 0xe2, 0x99, 0x81];
        ReadOnlySpan<byte> magic2 = [0x81, 0x99, 0xe2, 0x6d];
        ReadOnlySpan<byte> magic3 = [0x6d, 0xe2, 0x99, 0x45];
        ReadOnlySpan<byte> magic4 = [0x45, 0x99, 0xe2, 0x6d];

        return span[..4].SequenceEqual(magic1) ||
               span[..4].SequenceEqual(magic2) ||
               span[..4].SequenceEqual(magic3) ||
               span[..4].SequenceEqual(magic4);
    }

    private static async Task TerminateProcessTreeAsync(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(cts.Token);
            }
        }
        catch
        {
            // Process may already have exited
        }
    }

    private static async Task<string> ReadStderrAsync(Task<string>? stderrDrainTask)
    {
        if (stderrDrainTask is null)
        {
            return string.Empty;
        }

        try
        {
            var completed = await Task.WhenAny(stderrDrainTask, Task.Delay(2000));
            if (completed == stderrDrainTask)
            {
                return await stderrDrainTask;
            }
        }
        catch
        {
            // Ignore drain errors
        }

        return string.Empty;
    }

    private static void SanitizeChildEnvironment(ProcessStartInfo startInfo)
    {
        var keysToRemove = startInfo.Environment.Keys
            .Cast<string>()
            .Where(k => k.StartsWith("ROLERADAR_", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var k in keysToRemove)
        {
            startInfo.Environment.Remove(k);
        }
    }

    private static string ResolveTargetBackupFilePath(string targetPath)
    {
        if (Directory.Exists(targetPath) || targetPath.EndsWith(Path.DirectorySeparatorChar) || targetPath.EndsWith(Path.AltDirectorySeparatorChar) || !Path.HasExtension(targetPath))
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var filename = $"roleradar-backup-{timestamp}.rrbak";
            return Path.Combine(targetPath, filename);
        }

        return targetPath;
    }

    private static string CreateAtomicToolsConfigFile(string connectionString)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"rr-tools-{Guid.NewGuid():N}.conf");
        var fileContent = $"uri: \"{connectionString.Trim()}\"\n";
        var contentBytes = Encoding.UTF8.GetBytes(fileContent);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            };

            using var fs = new FileStream(tempPath, options);
            fs.Write(contentBytes);
            fs.Flush();
        }
        else
        {
            using var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            fs.Write(contentBytes);
            fs.Flush();
        }

        // Register for cleanup on unexpected process exit
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        };

        return tempPath;
    }

    private static (string Exe, string Args) SplitCommand(string command)
    {
        var trimmed = command.Trim();
        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0)
        {
            return (trimmed, string.Empty);
        }

        return (trimmed[..firstSpace], trimmed[(firstSpace + 1)..].Trim());
    }

    private static ValueTask<int> ReadExactOrToEofAsync(
        Stream stream,
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var vt = stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), cancellationToken);
            if (!vt.IsCompletedSuccessfully)
            {
                return ReadExactOrToEofSlowAsync(stream, buffer, offset + totalRead, count - totalRead, totalRead, vt, cancellationToken);
            }

            var read = vt.Result;
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }
        return ValueTask.FromResult(totalRead);
    }

    private static async ValueTask<int> ReadExactOrToEofSlowAsync(
        Stream stream,
        byte[] buffer,
        int offset,
        int count,
        int initialTotal,
        ValueTask<int> pendingRead,
        CancellationToken cancellationToken)
    {
        var totalRead = initialTotal;
        var firstRead = await pendingRead;
        if (firstRead == 0)
        {
            return totalRead;
        }
        totalRead += firstRead;

        while (totalRead < initialTotal + count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset + (totalRead - initialTotal), count - (totalRead - initialTotal)), cancellationToken);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }
        return totalRead;
    }

    private sealed class PrefixStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly int _prefixLength;
        private int _prefixOffset;
        private readonly Stream _underlying;

        public PrefixStream(byte[] prefix, int prefixLength, Stream underlying)
        {
            _prefix = prefix;
            _prefixLength = prefixLength;
            _underlying = underlying;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => _underlying.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_prefixOffset < _prefixLength)
            {
                var available = _prefixLength - _prefixOffset;
                var toCopy = Math.Min(count, available);
                Array.Copy(_prefix, _prefixOffset, buffer, offset, toCopy);
                _prefixOffset += toCopy;
                return toCopy;
            }

            return _underlying.Read(buffer, offset, count);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_prefixOffset < _prefixLength)
            {
                var available = _prefixLength - _prefixOffset;
                var toCopy = Math.Min(buffer.Length, available);
                _prefix.AsMemory(_prefixOffset, toCopy).CopyTo(buffer);
                _prefixOffset += toCopy;
                return toCopy;
            }

            return await _underlying.ReadAsync(buffer, cancellationToken);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }
    }
}
