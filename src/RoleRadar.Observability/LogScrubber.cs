using System.Text.RegularExpressions;

namespace RoleRadar.Observability;

/// <summary>
/// The last line of defense against secrets in log output (research R-003). Code must not log secrets in the first
/// place; every record still passes through these rules before it is written:
/// <list type="number">
/// <item>Bearer and Basic credentials.</item>
/// <item>JWT-shaped strings.</item>
/// <item>User info in mongodb:// and mongodb+srv:// URIs.</item>
/// <item>Sensitive key=value pairs in free text (for example <c>refresh_token=...</c>).</item>
/// <item>JSON-shaped "key": "value" pairs whose key is sensitive (Duende logs raw authorize requests this way).</item>
/// <item>Query strings on URL-shaped values.</item>
/// <item>Values of structured properties whose key is sensitive (<see cref="IsSensitiveKey"/>).</item>
/// </list>
/// The web mirrors these rules in web/app/lib/observability.ts.
/// </summary>
public static partial class LogScrubber
{
    public const string Redacted = "[redacted]";

    private static readonly string[] SensitiveKeyFragments = ["password", "passphrase", "secret", "token", "authorization", "cookie"];
    private static readonly string[] SensitiveExactKeys = ["otp", "totp", "code", "body", "content", "appendedtext", "notebody", "state", "nonce", "codechallenge", "codeverifier"];

    [GeneratedRegex(@"\b(Bearer|Basic)\s+[^\s""',;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthCredential();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*", RegexOptions.CultureInvariant)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(mongodb(?:\+srv)?://)[^@/\s]+@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MongoUserInfo();

    [GeneratedRegex(@"\b([A-Za-z_-]*(?:password|passphrase|secret|token|authorization|cookie)[A-Za-z_-]*|otp|totp|code)=[^\s&""',;]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitivePair();

    [GeneratedRegex(@"""([A-Za-z_][A-Za-z0-9_-]*)""(\s*:\s*)""(?:[^""\\]|\\.)*""", RegexOptions.CultureInvariant)]
    private static partial Regex JsonPair();

    [GeneratedRegex(@"(?<=[A-Za-z0-9/._%-])\?[^\s""'<>]*=[^\s""'<>]*", RegexOptions.CultureInvariant)]
    private static partial Regex QueryString();

    /// <summary>Applies every free-text rule. Text without secrets comes back unchanged; null becomes empty.</summary>
    public static string ScrubText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var result = AuthCredential().Replace(text, m => $"{m.Groups[1].Value} {Redacted}");
        result = Jwt().Replace(result, Redacted);
        result = MongoUserInfo().Replace(result, m => $"{m.Groups[1].Value}{Redacted}@");
        result = QueryString().Replace(result, string.Empty);
        result = SensitivePair().Replace(result, m => $"{m.Groups[1].Value}={Redacted}");
        result = JsonPair().Replace(result, m => IsSensitiveKey(m.Groups[1].Value) ? $"\"{m.Groups[1].Value}\"{m.Groups[2].Value}\"{Redacted}\"" : m.Value);
        return result;
    }

    /// <summary>
    /// True for keys whose values are never logged. Matching ignores case, '-' and '_': keys containing password,
    /// passphrase, secret, token, authorization or cookie, and the exact keys otp, totp, code, body, content,
    /// appendedText, noteBody, state, nonce, code_challenge and code_verifier.
    /// </summary>
    public static bool IsSensitiveKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var normalized = key.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return SensitiveExactKeys.Contains(normalized, StringComparer.Ordinal)
            || SensitiveKeyFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal));
    }

    /// <summary>
    /// The value to export for a structured log field. Numbers and booleans keep their type. Text is scrubbed. Anything else, such as
    /// the object Duende passes as <c>{@details}</c> or a collection of objects, becomes scrubbed text (the way the console already
    /// writes it), because an exporter would otherwise serialise the object with its own ToString() and skip every text rule.
    /// Arrays of primitives keep their type; any other sequence becomes an array of scrubbed text.
    /// </summary>
    public static object? ScrubValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case string text:
                return ScrubText(text);
            case bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                return value;
            case System.Collections.IEnumerable sequence:
                if (value.GetType().GetElementType() is { IsPrimitive: true })
                {
                    return value;
                }

                return sequence.Cast<object?>().Select(element => ScrubText(Convert.ToString(ScrubValue(element), System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            default:
                return ScrubText(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The value to log for a structured property: redacted for a sensitive key, otherwise scrubbed text.</summary>
    public static string ScrubProperty(string key, object? value) =>
        IsSensitiveKey(key) ? Redacted : ScrubText(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
}
