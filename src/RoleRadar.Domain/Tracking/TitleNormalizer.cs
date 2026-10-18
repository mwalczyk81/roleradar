using System.Text;

namespace RoleRadar.Domain.Tracking;

public static class TitleNormalizer
{
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(title.Length);
        var inWhitespace = false;

        foreach (var ch in title)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                inWhitespace = false;
            }
            else if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                if (!inWhitespace && sb.Length > 0)
                {
                    sb.Append(' ');
                    inWhitespace = true;
                }
            }
        }

        var result = sb.ToString().TrimEnd();
        return result;
    }
}
