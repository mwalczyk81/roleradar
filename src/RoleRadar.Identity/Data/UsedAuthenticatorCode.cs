namespace RoleRadar.Identity.Data;

/// <summary>
/// The memory that a one-time code was accepted, so the same code is rejected a second time (feature 014, ADR 0042,
/// data-model.md). It holds an HMAC of the code, never the code. Temporary: delete it, the table and
/// <c>AuthenticatorCodeGuard</c> when ASP.NET Core Identity rejects a reused authenticator code itself.
/// </summary>
public sealed class UsedAuthenticatorCode
{
    /// <summary>The Identity user. Part of the key; cascade-deleted with the user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Always equal to <see cref="UserId"/>. Carried because Principle IX requires an owner id on every table this repository creates.</summary>
    public Guid OwnerId { get; set; }

    /// <summary>HMAC-SHA-256 of the submitted code, keyed by the user's authenticator key: 64 lower-case hexadecimal characters.</summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>When the code was accepted (UTC).</summary>
    public DateTimeOffset AcceptedAtUtc { get; set; }

    /// <summary><see cref="AcceptedAtUtc"/> plus 5 minutes (UTC). A code is valid for at most 150 seconds after acceptance.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
