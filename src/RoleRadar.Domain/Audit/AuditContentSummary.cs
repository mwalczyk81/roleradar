using System.Text;
using RoleRadar.Domain.Notes;

namespace RoleRadar.Domain.Audit;

public sealed record AuditContentSummary
{
    public const int MaxExcerptChars = 200;

    public string BodyHash { get; }

    public int ByteLength { get; }

    public string Status { get; }

    public string Excerpt { get; }

    public AuditContentSummary(string bodyHash, int byteLength, string status, string excerpt)
    {
        if (string.IsNullOrWhiteSpace(bodyHash))
        {
            throw new ArgumentException("Body hash is required.", nameof(bodyHash));
        }

        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), "Byte length cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ArgumentException("Status is required.", nameof(status));
        }

        ArgumentNullException.ThrowIfNull(excerpt);

        BodyHash = bodyHash;
        ByteLength = byteLength;
        Status = status;
        Excerpt = excerpt.Length <= MaxExcerptChars ? excerpt : excerpt[..MaxExcerptChars];
    }

    public static AuditContentSummary FromNote(Note note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var byteLength = Encoding.UTF8.GetByteCount(note.Body);
        var excerpt = note.Body.Length <= MaxExcerptChars ? note.Body : note.Body[..MaxExcerptChars];
        return new AuditContentSummary(
            note.CurrentBodyHash,
            byteLength,
            note.Status.ToWireString(),
            excerpt);
    }
}
