using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Notes;
using Xunit;

namespace RoleRadar.Tests.Unit.Notes;

public sealed class NoteLinkExtensionTests
{
    private static readonly string[] InterviewTags = ["interview"];

    [Fact]
    public void Note_LinkProperties_DefaultToNull_AndCanBeSupplied()
    {
        var companyId = Guid.NewGuid();
        var postingId = Guid.NewGuid();
        var applicationId = Guid.NewGuid();

        var note = Note.Create(
            ownerId: "owner-1",
            title: "Interview Follow-Up Notes",
            body: "Discussion with engineering leadership.",
            tags: InterviewTags,
            area: "tech",
            provenance: new ProvenanceMarker(ProvenanceKind.Stated, "2026-10-18"),
            createdAtUtc: DateTime.UtcNow,
            companyId: companyId,
            postingId: postingId,
            applicationId: applicationId);

        Assert.Equal(companyId, note.CompanyId);
        Assert.Equal(postingId, note.PostingId);
        Assert.Equal(applicationId, note.ApplicationId);
    }

    [Fact]
    public void Note_Pre002BsonDocumentWithoutLinkFields_DeserializesCleanlyWithNulls()
    {
        // Simulate a 001-era Note document with zero link fields
        var doc = new BsonDocument
        {
            ["_id"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["owner_id"] = "owner-1",
            ["title"] = "Legacy 001 Note",
            ["body"] = "Legacy note body without company or posting links.",
            ["current_body_hash"] = "0000000000000000000000000000000000000000000000000000000000000000",
            ["tags"] = new BsonArray { "legacy" },
            ["area"] = "notes",
            ["provenance"] = new BsonDocument
            {
                ["source"] = "manual",
                ["date"] = "2026-09-01"
            },
            ["status"] = "Active",
            ["version_token"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard),
            ["revision_count"] = 0,
            ["created_at_utc"] = new BsonDateTime(DateTime.UtcNow),
            ["updated_at_utc"] = new BsonDateTime(DateTime.UtcNow)
        };

        var note = BsonSerializer.Deserialize<Note>(doc);
        Assert.NotNull(note);
        Assert.Equal("Legacy 001 Note", note.Title);
        Assert.Null(note.CompanyId);
        Assert.Null(note.PostingId);
        Assert.Null(note.ApplicationId);
    }
}
