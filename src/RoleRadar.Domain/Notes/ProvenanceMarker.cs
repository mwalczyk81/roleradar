using System.Globalization;

namespace RoleRadar.Domain.Notes;

public enum ProvenanceKind
{
    Stated,
    Researched,
    Inferred
}

public static class ProvenanceKindExtensions
{
    public static string ToWireString(this ProvenanceKind kind) => kind switch
    {
        ProvenanceKind.Stated => "stated",
        ProvenanceKind.Researched => "researched",
        ProvenanceKind.Inferred => "inferred",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown provenance kind.")
    };

    public static ProvenanceKind FromWireString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Provenance kind cannot be null or whitespace.", nameof(value));
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "stated" => ProvenanceKind.Stated,
            "researched" => ProvenanceKind.Researched,
            "inferred" => ProvenanceKind.Inferred,
            _ => throw new ArgumentException(
                $"Unsupported provenance kind '{value}'. Expected 'stated', 'researched', or 'inferred'.",
                nameof(value))
        };
    }
}

public sealed record ProvenanceMarker
{
    public ProvenanceKind Kind { get; }

    public string Date { get; }

    public string? Qualifier { get; }

    public ProvenanceMarker(ProvenanceKind kind, string date, string? qualifier = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Invalid provenance kind.");
        }

        if (string.IsNullOrWhiteSpace(date) ||
            !DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            throw new ArgumentException($"Provenance date '{date}' must be a valid ISO-8601 date in 'YYYY-MM-DD' format.", nameof(date));
        }

        Kind = kind;
        Date = date.Trim();
        Qualifier = string.IsNullOrWhiteSpace(qualifier) ? null : qualifier.Trim();
    }
}
