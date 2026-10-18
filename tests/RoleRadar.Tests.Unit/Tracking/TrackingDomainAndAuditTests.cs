using System.Reflection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class TrackingDomainAndAuditTests
{
    private static readonly string[] ExpectedOwnershipTypeNames =
    [
        "Public", "Private", "PeBacked", "VcBacked", "Mutual", "Subsidiary", "Nonprofit", "Government", "Unknown"
    ];

    private static readonly string[] ExpectedApplicationEventTypeNames =
    [
        "Applied", "FollowUp", "Interview", "Offer", "OfferAccepted", "OfferDeclined", "Rejected", "Withdrawn"
    ];

    private static readonly string[] ExpectedSignalTypeNames =
    [
        "Repost", "NoSalary", "ParallelPosting", "Gone", "Phantom", "Deadline"
    ];

    private static readonly string[] ForbiddenContactSummaryPropertyNames =
    [
        "Name", "Role", "Company", "CompanyAffiliation", "Notes", "Email", "Phone"
    ];

    [Fact]
    public void TrackingEnums_ContainAllSpecMandatedValues()
    {
        // OwnershipType must contain PE, VC, mutual, subsidiary, nonprofit, etc.
        Assert.Equal(ExpectedOwnershipTypeNames, Enum.GetNames<OwnershipType>());

        // ApplicationEventType must contain the 8 specified events
        Assert.Equal(ExpectedApplicationEventTypeNames, Enum.GetNames<ApplicationEventType>());

        // SignalType must contain 6 kinds
        Assert.Equal(ExpectedSignalTypeNames, Enum.GetNames<SignalType>());
    }

    [Fact]
    public void Company_Create_ValidatesDescriptionLengthAndSetsNormalizedName()
    {
        var longDescription = new string('x', 301);
        Assert.Throws<ArgumentException>(() => Company.Create("owner-1", "Acme Software Corp", description: longDescription));

        var company = Company.Create("owner-1", "  Acme Software Corp  ", description: "Valid short description");
        Assert.Equal("acme software corp", company.NormalizedName);
        Assert.Equal(CompanyStatus.Neutral, company.Status);
    }

    [Fact]
    public void CareerSource_Create_DefaultsToActive_AndSupportsRetireAndReactivate()
    {
        var source = CareerSource.Create("owner-1", Guid.CreateVersion7(), "Greenhouse", "acmesoftware", "careers");
        Assert.True(source.IsActive, "CareerSource must default to active upon creation.");

        source.Retire();
        Assert.False(source.IsActive, "Retire() must set IsActive to false.");

        source.Reactivate();
        Assert.True(source.IsActive, "Reactivate() must set IsActive to true.");
    }

    [Fact]
    public void ContactAuditContentSummary_ContainsExclusivelyFieldNamesAndRecordId_ZeroContactPii()
    {
        var properties = typeof(ContactAuditContentSummary)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToArray();

        foreach (var prop in properties)
        {
            if (string.Equals(prop, "ChangedFieldNames", StringComparison.Ordinal))
            {
                continue;
            }

            Assert.DoesNotContain(prop, ForbiddenContactSummaryPropertyNames, StringComparer.OrdinalIgnoreCase);
        }

        var instance = new ContactAuditContentSummary(Guid.CreateVersion7(), ["role", "notes"], false, DateTimeOffset.UtcNow);
        Assert.NotNull(instance);
        Assert.Equal(2, instance.ChangedFieldNames.Count);
    }

    [Fact]
    public void ErrorCode_ContainsInvalidTransition_AndReusesStaleVersionToken()
    {
        Assert.True(Enum.IsDefined(typeof(ErrorCode), "InvalidTransition"));
        Assert.True(Enum.IsDefined(typeof(ErrorCode), "StaleVersionToken"));
    }
}
