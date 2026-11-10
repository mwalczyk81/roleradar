using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using RoleRadar.Identity.Data;

namespace RoleRadar.Identity.Services;

public sealed record OwnerProvisioningResult(
    Guid UserId,
    string Username,
    string AuthenticatorKey,
    string OtpAuthUri)
{
    /// <summary>
    /// The exact <c>sub</c> claim value Identity emits for this user: the user id in lowercase "D" format.
    /// The API compares it with <c>Owner:AllowedSubjects</c> using strict ordinal equality, so it must be pasted verbatim.
    /// </summary>
    public string Subject => UserId.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Provisions the single owner account and checks its one-time codes. Feature 014 (ADR 0042): codes are verified by the
/// ASP.NET Core Identity authenticator provider, a code that was already accepted is rejected by
/// <see cref="AuthenticatorCodeGuard"/>, keys come from <see cref="UserManager{TUser}.GenerateNewAuthenticatorKey"/> and the
/// enrolment URI from Otp.NET. No RFC 6238 or Base32 code lives here any more.
/// </summary>
public class OwnerProvisioningService(
    IdentityAppDbContext dbContext,
    UserManager<IdentityUser<Guid>> users,
    TimeProvider? clock = null)
{
    private readonly IdentityAppDbContext _dbContext = dbContext;
    private readonly UserManager<IdentityUser<Guid>> _users = users;
    private readonly AuthenticatorCodeGuard _guard = new(dbContext, clock ?? TimeProvider.System);
    private readonly PasswordHasher<IdentityUser<Guid>> _hasher = new();

    /// <summary>
    /// Builds the service for callers that have no dependency injection (the CLI and tests): a private service provider with
    /// the same lockout options as the host, the Identity stores over <paramref name="dbContext"/> and only the authenticator
    /// token provider. Nothing here touches the file system or the network.
    /// </summary>
    public static OwnerProvisioningService Create(IdentityAppDbContext dbContext, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(dbContext);
        services.AddIdentityCore<IdentityUser<Guid>>(options =>
            {
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<IdentityAppDbContext>()
            .AddTokenProvider<AuthenticatorTokenProvider<IdentityUser<Guid>>>(TokenOptions.DefaultAuthenticatorProvider);

#pragma warning disable CA2000 // The provider holds no unmanaged resource and the context it wraps is owned by the caller.
        var provider = services.BuildServiceProvider();
#pragma warning restore CA2000
        return new OwnerProvisioningService(dbContext, provider.GetRequiredService<UserManager<IdentityUser<Guid>>>(), clock);
    }

    public async Task<bool> OwnerExistsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Users.AnyAsync(cancellationToken);
    }

    public async Task<OwnerProvisioningResult> ProvisionOwnerAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Username cannot be null or whitespace.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ArgumentException("Password cannot be null or whitespace.", nameof(password));
        }

        if (await OwnerExistsAsync(cancellationToken))
        {
            throw new InvalidOperationException("An owner account has already been provisioned.");
        }

        var normalizedUsername = username.Trim();
        var user = new IdentityUser<Guid>
        {
            Id = Guid.CreateVersion7(),
            UserName = normalizedUsername,
            NormalizedUserName = normalizedUsername.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("D"),
            ConcurrencyStamp = Guid.NewGuid().ToString("D"),
            LockoutEnabled = true,
            TwoFactorEnabled = false
        };

        user.PasswordHash = _hasher.HashPassword(user, password);

        var authenticatorKey = _users.GenerateNewAuthenticatorKey();

        _dbContext.Users.Add(user);

        var token = new IdentityUserToken<Guid>
        {
            UserId = user.Id,
            LoginProvider = "[AspNetUserStore]",
            Name = "AuthenticatorKey",
            Value = authenticatorKey
        };

        _dbContext.UserTokens.Add(token);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var otpAuthUri = BuildOtpAuthUri(authenticatorKey, normalizedUsername);

        return new OwnerProvisioningResult(user.Id, normalizedUsername, authenticatorKey, otpAuthUri);
    }

    /// <summary>Whether a user with this id exists.</summary>
    public async Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await _dbContext.Users.AnyAsync(u => u.Id == userId, cancellationToken);

    /// <summary>The stored authenticator key of a user, or null when there is none.</summary>
    public async Task<string?> GetAuthenticatorKeyAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var token = await _dbContext.UserTokens.AsNoTracking().FirstOrDefaultAsync(
            t => t.UserId == userId && t.LoginProvider == "[AspNetUserStore]" && t.Name == "AuthenticatorKey",
            cancellationToken);
        return token?.Value;
    }

    /// <summary>
    /// Creates a user with a caller-chosen id, password and authenticator key and two-factor already on. Used by the
    /// demo only (feature 012); the interactive <see cref="ProvisionOwnerAsync"/> stays the production path.
    /// Refuses when any user already exists.
    /// </summary>
    public async Task<OwnerProvisioningResult> ProvisionOwnerWithIdAsync(
        Guid userId,
        string username,
        string password,
        string authenticatorKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatorKey);

        if (await OwnerExistsAsync(cancellationToken))
        {
            throw new InvalidOperationException("An owner account has already been provisioned.");
        }

        var normalizedUsername = username.Trim();
        var user = new IdentityUser<Guid>
        {
            Id = userId,
            UserName = normalizedUsername,
            NormalizedUserName = normalizedUsername.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("D"),
            ConcurrencyStamp = Guid.NewGuid().ToString("D"),
            LockoutEnabled = true,
            TwoFactorEnabled = true
        };
        user.PasswordHash = _hasher.HashPassword(user, password);

        _dbContext.Users.Add(user);
        _dbContext.UserTokens.Add(new IdentityUserToken<Guid>
        {
            UserId = user.Id,
            LoginProvider = "[AspNetUserStore]",
            Name = "AuthenticatorKey",
            Value = authenticatorKey
        });
        await _dbContext.SaveChangesAsync(cancellationToken);

        var otpAuthUri = BuildOtpAuthUri(authenticatorKey, normalizedUsername);
        return new OwnerProvisioningResult(user.Id, normalizedUsername, authenticatorKey, otpAuthUri);
    }

    /// <summary>
    /// Replaces a user's password and authenticator key, rotates the security stamp, turns two-factor on and clears any
    /// lockout. Returns false when the user does not exist. Used by the demo only.
    /// </summary>
    public async Task<bool> ResetCredentialsAsync(
        Guid userId,
        string password,
        string authenticatorKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatorKey);

        var user = await _dbContext.Users.FindAsync([userId], cancellationToken: cancellationToken);
        if (user is null)
        {
            return false;
        }

        user.PasswordHash = _hasher.HashPassword(user, password);
        user.SecurityStamp = Guid.NewGuid().ToString("D");
        user.TwoFactorEnabled = true;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;

        var token = await _dbContext.UserTokens.FirstOrDefaultAsync(
            t => t.UserId == userId && t.LoginProvider == "[AspNetUserStore]" && t.Name == "AuthenticatorKey",
            cancellationToken);
        if (token is null)
        {
            _dbContext.UserTokens.Add(new IdentityUserToken<Guid>
            {
                UserId = userId,
                LoginProvider = "[AspNetUserStore]",
                Name = "AuthenticatorKey",
                Value = authenticatorKey
            });
        }
        else
        {
            token.Value = authenticatorKey;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Checks a one-time code for a user and, when it is good, switches two-factor on. The platform authenticator provider
    /// decides whether the code is valid (the current step and two steps either side); <see cref="AuthenticatorCodeGuard"/>
    /// then rejects a code that was already accepted. Returns false for an unknown user, a missing key, a wrong code and a
    /// replay alike.
    /// </summary>
    public async Task<bool> VerifyTotpAndActivateAsync(
        Guid userId,
        string totpCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(totpCode))
        {
            return false;
        }

        var user = await _dbContext.Users.FindAsync([userId], cancellationToken: cancellationToken);
        if (user is null)
        {
            return false;
        }

        var key = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var code = totpCode.Trim();
        if (!await _users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
        {
            return false;
        }

        if (!await _guard.TryRecordAsync(user.Id, key, code, cancellationToken))
        {
            return false;
        }

        if (!user.TwoFactorEnabled)
        {
            user.TwoFactorEnabled = true;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private static string BuildOtpAuthUri(string authenticatorKey, string username) =>
        new OtpUri(OtpType.Totp, authenticatorKey, username, "RoleRadar").ToString();
}
