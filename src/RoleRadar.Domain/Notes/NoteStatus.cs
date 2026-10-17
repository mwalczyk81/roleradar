namespace RoleRadar.Domain.Notes;

public enum NoteStatus
{
    Active,
    Archived
}

public static class NoteStatusExtensions
{
    public static string ToWireString(this NoteStatus status) => status switch
    {
        NoteStatus.Active => "active",
        NoteStatus.Archived => "archived",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown note status.")
    };

    public static NoteStatus FromWireString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Note status cannot be null or whitespace.", nameof(value));
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "active" => NoteStatus.Active,
            "archived" => NoteStatus.Archived,
            _ => throw new ArgumentException($"Unsupported note status '{value}'. Expected 'active' or 'archived'.", nameof(value))
        };
    }
}
