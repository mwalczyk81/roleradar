using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Unit.Tracking;

public sealed class CompanyDomainTests
{
    [Fact]
    public void Company_Create_ValidInputs_InitializesProperly()
    {
        var id = Guid.CreateVersion7();
        var company = Company.Create(
            ownerId: "owner-1",
            name: "Acme Software",
            description: "Compliance software",
            parentCompany: "Acme Holdings",
            ownershipType: OwnershipType.VcBacked,
            headquarters: "Austin, TX",
            categoryTags: ["cloud", "security", "Cloud"], // Case-insensitive duplicate
            status: CompanyStatus.Target,
            id: id);

        Assert.Equal(id, company.Id);
        Assert.Equal("owner-1", company.OwnerId);
        Assert.Equal("Acme Software", company.Name);
        Assert.Equal("acme software", company.NormalizedName);
        Assert.Equal("Compliance software", company.Description);
        Assert.Equal("Acme Holdings", company.ParentCompany);
        Assert.Equal(OwnershipType.VcBacked, company.OwnershipType);
        Assert.Equal("Austin, TX", company.Headquarters);
        Assert.Equal(CompanyStatus.Target, company.Status);
        Assert.Equal(2, company.CategoryTags.Count);
        Assert.Contains("cloud", company.CategoryTags);
        Assert.Contains("security", company.CategoryTags);
        Assert.NotEqual(Guid.Empty, company.VersionToken);
    }

    [Fact]
    public void Company_Create_ExceedingLengthLimits_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            Company.Create("owner-1", new string('a', 151)));

        Assert.Throws<ArgumentException>(() =>
            Company.Create("owner-1", "Valid Name", description: new string('d', 301)));

        Assert.Throws<ArgumentException>(() =>
            Company.Create("owner-1", "Valid Name", parentCompany: new string('p', 151)));

        Assert.Throws<ArgumentException>(() =>
            Company.Create("owner-1", "Valid Name", headquarters: new string('h', 151)));

        Assert.Throws<ArgumentException>(() =>
            Company.Create("owner-1", "Valid Name", categoryTags: Enumerable.Range(0, 21).Select(i => $"tag{i}").ToArray()));

        Assert.Throws<ArgumentException>(() =>
            Company.Create("owner-1", "Valid Name", categoryTags: [new string('t', 51)]));
    }

    [Fact]
    public void Company_Update_ModifiesFieldsAndRotatesVersionToken()
    {
        var company = Company.Create("owner-1", "Initial Name");
        var initialToken = company.VersionToken;

        company.Update(
            name: "Updated Name",
            description: "Updated description",
            ownershipType: OwnershipType.PeBacked,
            status: CompanyStatus.Watch,
            categoryTags: ["new-tag"]);

        Assert.Equal("Updated Name", company.Name);
        Assert.Equal("updated name", company.NormalizedName);
        Assert.Equal("Updated description", company.Description);
        Assert.Equal(OwnershipType.PeBacked, company.OwnershipType);
        Assert.Equal(CompanyStatus.Watch, company.Status);
        Assert.Single(company.CategoryTags);
        Assert.Equal("new-tag", company.CategoryTags[0]);
        Assert.NotEqual(initialToken, company.VersionToken);
    }

    [Fact]
    public void CareerSource_Create_ValidInputs_InitializesProperly()
    {
        var companyId = Guid.CreateVersion7();
        var cs = CareerSource.Create(
            ownerId: "owner-1",
            companyId: companyId,
            atsType: "Greenhouse",
            tenant: "acme",
            slug: "careers",
            fetchMethod: FetchMethod.Api,
            notes: "API endpoint",
            isActive: true);

        Assert.Equal("owner-1", cs.OwnerId);
        Assert.Equal(companyId, cs.CompanyId);
        Assert.Equal("Greenhouse", cs.AtsType);
        Assert.Equal("acme", cs.Tenant);
        Assert.Equal("careers", cs.Slug);
        Assert.Equal(FetchMethod.Api, cs.FetchMethod);
        Assert.Equal("API endpoint", cs.Notes);
        Assert.True(cs.IsActive);
        Assert.NotEqual(Guid.Empty, cs.VersionToken);
    }

    [Fact]
    public void CareerSource_RetireAndReactivate_TogglesActiveAndRotatesToken()
    {
        var cs = CareerSource.Create("owner-1", Guid.CreateVersion7(), "Workday");
        var token1 = cs.VersionToken;
        Assert.True(cs.IsActive);

        cs.Retire();
        Assert.False(cs.IsActive);
        Assert.NotEqual(token1, cs.VersionToken);

        var token2 = cs.VersionToken;
        cs.Reactivate();
        Assert.True(cs.IsActive);
        Assert.NotEqual(token2, cs.VersionToken);
    }

    [Theory]
    [InlineData("public", OwnershipType.Public)]
    [InlineData("private", OwnershipType.Private)]
    [InlineData("pe_backed", OwnershipType.PeBacked)]
    [InlineData("pebacked", OwnershipType.PeBacked)]
    [InlineData("vc_backed", OwnershipType.VcBacked)]
    [InlineData("vcbacked", OwnershipType.VcBacked)]
    [InlineData("mutual", OwnershipType.Mutual)]
    [InlineData("subsidiary", OwnershipType.Subsidiary)]
    [InlineData("nonprofit", OwnershipType.Nonprofit)]
    [InlineData("government", OwnershipType.Government)]
    [InlineData("unknown", OwnershipType.Unknown)]
    public void TrackingEnumExtensions_OwnershipType_RoundTrips(string text, OwnershipType expected)
    {
        var parsed = TrackingEnumExtensions.TryParseOwnershipType(text, out var actual);
        Assert.True(parsed);
        Assert.Equal(expected, actual);
        Assert.Equal(expected.ToSnakeCase(), actual.ToSnakeCase());
    }

    [Theory]
    [InlineData("target", CompanyStatus.Target)]
    [InlineData("watch", CompanyStatus.Watch)]
    [InlineData("avoid", CompanyStatus.Avoid)]
    [InlineData("neutral", CompanyStatus.Neutral)]
    public void TrackingEnumExtensions_CompanyStatus_RoundTrips(string text, CompanyStatus expected)
    {
        var parsed = TrackingEnumExtensions.TryParseCompanyStatus(text, out var actual);
        Assert.True(parsed);
        Assert.Equal(expected, actual);
        Assert.Equal(expected.ToSnakeCase(), actual.ToSnakeCase());
    }

    [Theory]
    [InlineData("api", FetchMethod.Api)]
    [InlineData("browser", FetchMethod.Browser)]
    [InlineData("blocked", FetchMethod.Blocked)]
    public void TrackingEnumExtensions_FetchMethod_RoundTrips(string text, FetchMethod expected)
    {
        var parsed = TrackingEnumExtensions.TryParseFetchMethod(text, out var actual);
        Assert.True(parsed);
        Assert.Equal(expected, actual);
        Assert.Equal(expected.ToSnakeCase(), actual.ToSnakeCase());
    }
}
