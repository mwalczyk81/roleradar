using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;

namespace RoleRadar.Cli.Demo;

/// <summary>Result of making sure the demo account exists. Deliberately an enum: it can carry no credential.</summary>
public enum DemoAccountEnsureOutcome
{
    Created,
    AlreadyExists,
    RefusedOtherUserExists
}

/// <summary>Credentials of the demo account, produced only by a reset and printed once by the credentials command.</summary>
public sealed record DemoCredentials(string Username, string Password, string AuthenticatorKey, string OtpAuthUri);

/// <summary>The current TOTP code of the demo account and how long it stays valid.</summary>
public sealed record DemoTotp(string Code, int SecondsRemaining);

/// <summary>
/// Creates, resets and reads the one demo account through the existing owner provisioning service. The password and key
/// of a freshly seeded account are generated and discarded; only a reset returns them (FR-006, FR-007a).
/// </summary>
public sealed class DemoAccountManager(IdentityAppDbContext dbContext)
{
    private readonly OwnerProvisioningService _service = new(dbContext);

    private static Guid UserId => Guid.Parse(DemoAccount.UserId);

    public async Task<DemoAccountEnsureOutcome> EnsureAccountAsync(CancellationToken cancellationToken = default)
    {
        if (await _service.UserExistsAsync(UserId, cancellationToken))
        {
            return DemoAccountEnsureOutcome.AlreadyExists;
        }

        if (await _service.OwnerExistsAsync(cancellationToken))
        {
            return DemoAccountEnsureOutcome.RefusedOtherUserExists;
        }

        // The generated values are intentionally dropped: nothing prints or logs them. `demo credentials` resets them.
        await _service.ProvisionOwnerWithIdAsync(
            UserId,
            DemoAccount.Username,
            CredentialGenerator.GeneratePassword(),
            CredentialGenerator.GenerateAuthenticatorKey(),
            cancellationToken);
        return DemoAccountEnsureOutcome.Created;
    }

    public async Task<DemoCredentials?> ResetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        var password = CredentialGenerator.GeneratePassword();
        var key = CredentialGenerator.GenerateAuthenticatorKey();
        if (!await _service.ResetCredentialsAsync(UserId, password, key, cancellationToken))
        {
            return null;
        }

        var uri = $"otpauth://totp/RoleRadar:{Uri.EscapeDataString(DemoAccount.Username)}?secret={key}&issuer=RoleRadar";
        return new DemoCredentials(DemoAccount.Username, password, key, uri);
    }

    public async Task<DemoTotp?> CurrentTotpAsync(CancellationToken cancellationToken = default)
    {
        var key = await _service.GetAuthenticatorKeyAsync(UserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new DemoTotp(_service.ComputeTotpCode(key, now), (int)(30 - (now % 30)));
    }
}
