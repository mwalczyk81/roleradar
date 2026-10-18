using System.Reflection;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class ContactPrivacyTests
{
    private static readonly string[] ForbiddenFieldNames =
    [
        "Name", "Role", "Company", "CompanyAffiliation", "Notes", "Email", "Phone", "Description"
    ];

    [Fact]
    public void ContactAuditContentSummary_ContainsExclusivelyFieldNamesAndRecordId_ZeroContactPii()
    {
        var properties = typeof(ContactAuditContentSummary)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        foreach (var prop in properties)
        {
            if (string.Equals(prop, "ChangedFieldNames", StringComparison.Ordinal) ||
                string.Equals(prop, "ContactId", StringComparison.Ordinal) ||
                string.Equals(prop, "IsRedacted", StringComparison.Ordinal) ||
                string.Equals(prop, "Timestamp", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.DoesNotContain(prop, ForbiddenFieldNames, StringComparer.OrdinalIgnoreCase);
        }

        var summary = new ContactAuditContentSummary(
            ContactId: Guid.NewGuid(),
            ChangedFieldNames: ["name", "role", "company_affiliation", "relationship", "notes"],
            IsRedacted: false,
            Timestamp: DateTimeOffset.UtcNow);

        Assert.Equal(5, summary.ChangedFieldNames.Count);
        Assert.False(summary.IsRedacted);
    }

    [Fact]
    public void Contact_Create_And_Redact_EnforcesPrivacyInvariants()
    {
        var contactId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var contact = Contact.Create(
            ownerId: "owner-1",
            name: "Dr. Eleanor Vance",
            role: "Principal AI Researcher",
            companyAffiliation: "Omni Research Labs",
            companyId: companyId,
            relationship: ContactRelationship.Referral,
            notes: "Collaborated on multi-agent systems paper.",
            id: contactId);

        Assert.Equal("Dr. Eleanor Vance", contact.Name);
        Assert.Equal("Principal AI Researcher", contact.Role);
        Assert.Equal("Omni Research Labs", contact.CompanyAffiliation);
        Assert.Equal(companyId, contact.CompanyId);
        Assert.Equal(ContactRelationship.Referral, contact.Relationship);
        Assert.Equal("Collaborated on multi-agent systems paper.", contact.Notes);
        Assert.False(contact.IsRedacted);

        // Execute redaction
        var oldToken = contact.VersionToken;
        contact.Redact(DateTimeOffset.UtcNow);

        Assert.Equal("[REDACTED]", contact.Name);
        Assert.Null(contact.Role);
        Assert.Null(contact.CompanyAffiliation);
        Assert.Null(contact.CompanyId);
        Assert.Equal(ContactRelationship.Redacted, contact.Relationship);
        Assert.Null(contact.Notes);
        Assert.True(contact.IsRedacted);
        Assert.NotEqual(oldToken, contact.VersionToken);
        Assert.Equal(contactId, contact.Id);
    }
}
