using System.Security.Cryptography;
using System.Text;

namespace RoleRadar.Domain.Common;

public static class Sha256Hasher
{
    /// <summary>
    /// Computes the lowercase hex SHA-256 of the UTF-8 bytes of the supplied string.
    /// </summary>
    public static string ComputeLowerHex(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var utf8Bytes = Encoding.UTF8.GetBytes(content);
        return ComputeLowerHex(utf8Bytes);
    }

    /// <summary>
    /// Computes the lowercase hex SHA-256 of the supplied byte span.
    /// </summary>
    public static string ComputeLowerHex(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, digest);
        return Convert.ToHexStringLower(digest);
    }
}
