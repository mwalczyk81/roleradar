using System.Security.Cryptography;
using OtpNet;

namespace RoleRadar.Cli.Demo;

/// <summary>Generates the demo account's password and TOTP key. Nothing here logs or stores anything.</summary>
public static class CredentialGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "-_+=!?*";
    private const int Length = 24;

    /// <summary>A random 24 character password with at least one upper, lower, digit and symbol character.</summary>
    public static string GeneratePassword()
    {
        var all = Upper + Lower + Digits + Symbols;
        var chars = new List<char>
        {
            Pick(Upper), Pick(Lower), Pick(Digits), Pick(Symbols)
        };
        while (chars.Count < Length)
        {
            chars.Add(Pick(all));
        }

        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string([.. chars]);
    }

    /// <summary>A random Base32 authenticator key, in the format the interactive provisioning uses.</summary>
    public static string GenerateAuthenticatorKey() => Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
