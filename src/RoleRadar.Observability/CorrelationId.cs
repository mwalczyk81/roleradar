using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace RoleRadar.Observability;

/// <summary>
/// Rules for the request correlation id carried in <see cref="HeaderName"/> (ADR 0041,
/// specs/013-operability/contracts/http-correlation-and-errors.md). The id is opaque: it is not an entity id and
/// not a tracing span.
/// </summary>
public static partial class CorrelationId
{
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>The logging scope key, and the JSON log field name in camelCase (<c>correlationId</c>).</summary>
    public const string ScopeKey = "CorrelationId";

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{7,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Accepted();

    /// <summary>
    /// Accepts a well-formed inbound value (8 to 64 characters, letters, digits, '.', '_' and '-', starting with a
    /// letter or digit). Anything else is rejected and must be replaced with <see cref="New"/>, never logged.
    /// </summary>
    public static bool TryAccept(string? candidate, [NotNullWhen(true)] out string? correlationId)
    {
        if (candidate is not null && Accepted().IsMatch(candidate))
        {
            correlationId = candidate;
            return true;
        }

        correlationId = null;
        return false;
    }

    /// <summary>A new id: a UUIDv7 in "D" format.</summary>
    public static string New() => Guid.CreateVersion7().ToString("D");
}
