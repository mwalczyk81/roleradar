using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RoleRadar.Identity.Data;

namespace RoleRadar.Identity.Services;

/// <summary>
/// Rejects a one-time code that was already accepted (feature 014, ADR 0042, FR-015). RFC 6238 section 5.2 says the verifier
/// MUST NOT accept the second attempt of an OTP, and the ASP.NET Core Identity authenticator provider does accept it
/// (dotnet/aspnetcore#14423, documented rather than fixed upstream). This is a thin, temporary extension around the
/// provider: it is called only after the provider has accepted a code, records the code for that user, and says whether the
/// record is new. Delete it when upstream rejects reuse itself; <c>AuthenticatorProviderBehaviorTests</c> will fail then.
/// </summary>
/// <remarks>
/// The record holds an HMAC-SHA-256 of the code keyed by the user's authenticator key, never the code. The primary key
/// (user id, code hash) makes two simultaneous attempts safe: exactly one insert wins. Rows are kept for five minutes (a
/// code stays valid for at most 150 seconds after acceptance) and are removed by the next insert.
/// </remarks>
public sealed class AuthenticatorCodeGuard(IdentityAppDbContext dbContext, TimeProvider clock)
{
    /// <summary>How long a used code is remembered. A code is valid for at most 150 seconds after it is accepted.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);

    private const int SqliteConstraint = 19;
    private const int SqlitePrimaryKeyViolation = 1555;
    private const int SqliteUniqueViolation = 2067;

    /// <summary>
    /// Records <paramref name="code"/> as used by <paramref name="userId"/>. Returns true when it was newly recorded and false
    /// when it was already recorded, that is, when this attempt is a replay.
    /// </summary>
    public async Task<bool> TryRecordAsync(Guid userId, string authenticatorKey, string code, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatorKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var now = clock.GetUtcNow();
        await dbContext.UsedAuthenticatorCodes
            .Where(used => used.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);

        var entry = dbContext.UsedAuthenticatorCodes.Add(new UsedAuthenticatorCode
        {
            UserId = userId,
            OwnerId = userId,
            CodeHash = Hash(authenticatorKey, code),
            AcceptedAtUtc = now,
            ExpiresAtUtc = now + Retention,
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicate(exception))
        {
            // Another attempt, or an earlier one, already recorded this code: a replay.
            return false;
        }
        finally
        {
            // The row is only ever read through the database; do not keep it in this context, or a second attempt in the same
            // context would collide in the change tracker before the unique key is ever checked.
            entry.State = EntityState.Detached;
        }
    }

    private static string Hash(string authenticatorKey, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(authenticatorKey), Encoding.ASCII.GetBytes(code))).ToLowerInvariant();

    private static bool IsDuplicate(DbUpdateException exception) =>
        exception.InnerException is SqliteException { SqliteErrorCode: SqliteConstraint, SqliteExtendedErrorCode: SqlitePrimaryKeyViolation or SqliteUniqueViolation };
}
