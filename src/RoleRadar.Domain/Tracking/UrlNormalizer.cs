using System.Text;

namespace RoleRadar.Domain.Tracking;

public static class UrlNormalizer
{
    private static readonly HashSet<string> ExactDenylist = new(StringComparer.OrdinalIgnoreCase)
    {
        "ref",
        "source",
        "gh_src",
        "src",
        "source_id",
        "fbclid",
        "gclid",
        "gclsrc",
        "dclid",
        "msclkid",
        "twclid",
        "trk",
        "trackingid",
        "tracking_id",
        "li_fat_id",
        "mc_cid",
        "mc_eid",
        "_hsenc",
        "_hsmi",
        "hsctatracking",
        "igshid",
        "yclid",
        "mkt_tok"
    };

    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return trimmed;
        }

        var scheme = uri.Scheme.ToLowerInvariant();
        var host = uri.Host.ToLowerInvariant();
        var port = uri.Port;

        var sb = new StringBuilder();
        sb.Append(scheme);
        sb.Append("://");
        sb.Append(host);

        var isDefaultPort = (scheme == "http" && port == 80) || (scheme == "https" && port == 443);
        if (!isDefaultPort && port > 0)
        {
            sb.Append(':');
            sb.Append(port);
        }

        var path = uri.AbsolutePath;
        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path.TrimEnd('/');
        }
        else if (path == "/")
        {
            path = string.Empty;
        }

        sb.Append(path);

        var query = uri.Query;
        if (!string.IsNullOrEmpty(query))
        {
            var rawQuery = query.StartsWith('?') ? query[1..] : query;
            var parts = rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries);
            var keptParams = new List<string>();

            foreach (var part in parts)
            {
                var equalsIndex = part.IndexOf('=');
                var paramName = equalsIndex >= 0 ? part[..equalsIndex] : part;

                if (IsTrackingParam(paramName))
                {
                    continue;
                }

                keptParams.Add(part);
            }

            if (keptParams.Count > 0)
            {
                sb.Append('?');
                sb.Append(string.Join("&", keptParams));
            }
        }

        return sb.ToString();
    }

    private static bool IsTrackingParam(string name)
    {
        if (name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.StartsWith("ref_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ExactDenylist.Contains(name);
    }
}
