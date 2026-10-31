using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RoleRadar.Identity.Data;

namespace RoleRadar.Identity.Services;

public sealed record OwnerProvisioningResult(
    Guid UserId,
    string Username,
    string AuthenticatorKey,
    string OtpAuthUri);

public class OwnerProvisioningService(IdentityAppDbContext dbContext)
{
    private readonly IdentityAppDbContext _dbContext = dbContext;
    private readonly PasswordHasher<IdentityUser<Guid>> _hasher = new();

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

        var authenticatorKey = GenerateBase32Secret(20);

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

        var otpAuthUri = $"otpauth://totp/RoleRadar:{Uri.EscapeDataString(normalizedUsername)}?secret={authenticatorKey}&issuer=RoleRadar";

        return new OwnerProvisioningResult(user.Id, normalizedUsername, authenticatorKey, otpAuthUri);
    }

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

        var token = await _dbContext.UserTokens.FirstOrDefaultAsync(
            t => t.UserId == userId && t.LoginProvider == "[AspNetUserStore]" && t.Name == "AuthenticatorKey",
            cancellationToken);

        if (token is null || string.IsNullOrWhiteSpace(token.Value))
        {
            return false;
        }

        var code = totpCode.Trim();
        if (!VerifyTotp(token.Value, code))
        {
            return false;
        }

        user.TwoFactorEnabled = true;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public string ComputeTotpCode(string base32Secret, long? timestampSeconds = null)
    {
        var timestamp = timestampSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var step = timestamp / 30;
        return ComputeTotpForStep(base32Secret, step);
    }

    private static bool VerifyTotp(string base32Secret, string code)
    {
        var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var currentStep = currentTimestamp / 30;

        // Allow ±1 time step tolerance (RFC 6238 recommendation)
        for (var offset = -1; offset <= 1; offset++)
        {
            var expectedCode = ComputeTotpForStep(base32Secret, currentStep + offset);
            if (CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expectedCode),
                Encoding.ASCII.GetBytes(code)))
            {
                return true;
            }
        }

        return false;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms", Justification = "RFC 6238 TOTP standard mandates HMAC-SHA1 for authenticator app interoperability.")]
    private static string ComputeTotpForStep(string base32Secret, long step)
    {
        var key = Base32Decode(base32Secret);
        Span<byte> stepBytes = stackalloc byte[8];
        for (var i = 7; i >= 0; i--)
        {
            stepBytes[i] = (byte)(step & 0xFF);
            step >>= 8;
        }

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(stepBytes.ToArray());

        var offset = hash[^1] & 0x0F;
        var binaryCode = ((hash[offset] & 0x7F) << 24)
                       | ((hash[offset + 1] & 0xFF) << 16)
                       | ((hash[offset + 2] & 0xFF) << 8)
                       | (hash[offset + 3] & 0xFF);

        var otp = binaryCode % 1_000_000;
        return otp.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string GenerateBase32Secret(int byteLength)
    {
        var bytes = new byte[byteLength];
        RandomNumberGenerator.Fill(bytes);
        return Base32Encode(bytes);
    }

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    private static string Base32Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int currentByte = 0, bitsRemaining = 8, mask = 0;

        for (var i = 0; i < data.Length;)
        {
            if (bitsRemaining >= 5)
            {
                mask = (data[i] >> (bitsRemaining - 5)) & 0x1F;
                sb.Append(Base32Alphabet[mask]);
                bitsRemaining -= 5;
                if (bitsRemaining == 0)
                {
                    bitsRemaining = 8;
                    i++;
                }
            }
            else
            {
                currentByte = (data[i] & ((1 << bitsRemaining) - 1)) << (5 - bitsRemaining);
                i++;
                if (i < data.Length)
                {
                    currentByte |= (data[i] >> (8 - (5 - bitsRemaining))) & 0x1F;
                    bitsRemaining = 8 - (5 - bitsRemaining);
                }
                sb.Append(Base32Alphabet[currentByte & 0x1F]);
            }
        }

        return sb.ToString();
    }

    private static byte[] Base32Decode(string base32)
    {
        var cleanInput = base32.Trim().ToUpperInvariant();
        var output = new List<byte>();
        var buffer = 0;
        var bitsLeft = 0;

        foreach (var c in cleanInput)
        {
            var val = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (val < 0) continue;

            buffer = (buffer << 5) | val;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return [.. output];
    }
}
