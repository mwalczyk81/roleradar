using System.Globalization;
using System.Text.RegularExpressions;

namespace RoleRadar.Infrastructure.Mongo;

/// <summary>
/// Safety guard for validating, generating, and ensuring safety when managing ephemeral
/// scratch databases for tracker-import dry-run execution.
/// </summary>
public static partial class MongoScratchDatabaseGuard
{
    private const string Prefix = "roleradar_scratch_";

    [GeneratedRegex(@"^roleradar_scratch_[0-9a-f]{32}\z", RegexOptions.Compiled)]
    private static partial Regex ScratchDatabaseRegex();

    /// <summary>
    /// Validates whether a database name strictly adheres to the scratch database naming format:
    /// ^roleradar_scratch_[0-9a-f]{32}\z
    /// </summary>
    public static bool IsValidScratchDatabaseName(string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return false;
        }

        return ScratchDatabaseRegex().IsMatch(databaseName);
    }

    /// <summary>
    /// Asserts that a database is safe to drop: it must match the strict scratch database pattern
    /// and must never be the target database. Throws InvalidOperationException if safety checks fail.
    /// </summary>
    public static void AssertSafeToDrop(string scratchDatabaseName, string targetDatabaseName)
    {
        if (string.IsNullOrWhiteSpace(scratchDatabaseName))
        {
            throw new InvalidOperationException("Scratch database name cannot be null or empty.");
        }

        if (string.Equals(scratchDatabaseName, targetDatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to drop database '{scratchDatabaseName}': target database must never be dropped.");
        }

        if (!IsValidScratchDatabaseName(scratchDatabaseName))
        {
            throw new InvalidOperationException(
                $"Refusing to drop database '{scratchDatabaseName}': name does not match required scratch database pattern '^roleradar_scratch_[0-9a-f]{{32}}\\z'.");
        }
    }

    /// <summary>
    /// Determines whether an existing database in the cluster is eligible for orphan cleanup:
    /// it must match the scratch database pattern, must not be the target database, and must be
    /// older than minimumAge (default: 1 hour).
    /// </summary>
    public static bool IsEligibleForOrphanCleanup(
        string databaseName,
        string targetDatabaseName,
        DateTimeOffset creationTime,
        DateTimeOffset now,
        TimeSpan? minimumAge = null)
    {
        if (string.Equals(databaseName, targetDatabaseName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!IsValidScratchDatabaseName(databaseName))
        {
            return false;
        }

        var ageThreshold = minimumAge ?? TimeSpan.FromHours(1);
        var age = now - creationTime;

        return age >= ageThreshold;
    }

    /// <summary>
    /// Generates a new unique scratch database name using UUIDv7 (embedding creation timestamp).
    /// </summary>
    public static string GenerateScratchDatabaseName()
    {
        var guid = Guid.CreateVersion7();
        return $"{Prefix}{guid:N}";
    }

    /// <summary>
    /// Attempts to extract the creation timestamp from a UUIDv7-based scratch database name.
    /// In UUIDv7 string format (32 hex characters), the first 12 characters contain the 48-bit timestamp in milliseconds.
    /// </summary>
    public static bool TryExtractCreationTime(string databaseName, out DateTimeOffset creationTime)
    {
        creationTime = default;

        if (!IsValidScratchDatabaseName(databaseName))
        {
            return false;
        }

        var hex = databaseName[Prefix.Length..];
        if (!Guid.TryParseExact(hex, "N", out var guid))
        {
            return false;
        }

        // In RFC 9562 UUIDv7, bytes 0..5 (in big-endian wire format) represent unix_ts_ms (48 bits).
        // Using byte array with big-endian flag:
        Span<byte> bytes = stackalloc byte[16];
        if (!guid.TryWriteBytes(bytes, bigEndian: true, out _))
        {
            return false;
        }

        long unixMs = ((long)bytes[0] << 40) |
                      ((long)bytes[1] << 32) |
                      ((long)bytes[2] << 24) |
                      ((long)bytes[3] << 16) |
                      ((long)bytes[4] << 8) |
                      bytes[5];

        if (unixMs <= 0)
        {
            return false;
        }

        try
        {
            creationTime = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
