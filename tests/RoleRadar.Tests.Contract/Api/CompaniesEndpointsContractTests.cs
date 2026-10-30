using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

public sealed class CompaniesEndpointsContractTests(SharedMongoFixture sharedMongo)
{
    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

    [Fact]
    public async Task ListAndGetCompany_AndPatchCompany_PreservesOmittedFields()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_companies");
        using var scope = host.Services.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();

        var created = await companyHandler.HandleAsync(new RecordCompanyCommand(
            Name: "Helios Data Labs",
            Description: "Real-time streaming platform",
            OwnershipType: "private",
            Headquarters: "Chicago, IL",
            Status: "neutral",
            CategoryTags: ["streaming", "data"],
            CareerSources:
            [
                new CareerSourceInputDto(
                    AtsType: "greenhouse",
                    Tenant: "helios",
                    Slug: "helios",
                    FetchMethod: "api",
                    IsActive: true)
            ]));

        Assert.True(created.IsSuccess);
        var companyId = created.Value!.Company.Id;

        // 1. GET /api/v1/companies
        var listResp = await host.Client.GetAsync("/api/v1/companies?limit=10&offset=0");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listJson = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, listJson.GetProperty("totalCount").GetInt32());

        // 2. GET /api/v1/companies/{id}
        var getResp = await host.Client.GetAsync($"/api/v1/companies/{companyId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var detailJson = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Helios Data Labs", detailJson.GetProperty("name").GetString());
        Assert.Equal("Real-time streaming platform", detailJson.GetProperty("description").GetString());
        Assert.Equal("Chicago, IL", detailJson.GetProperty("headquarters").GetString());

        // 3. PATCH /api/v1/companies/{id} with only status updated; omitted fields must remain unchanged
        var patchPayload = new
        {
            status = "target"
        };

        var patchResp = await host.Client.PatchAsJsonAsync($"/api/v1/companies/{companyId}", patchPayload);
        Assert.Equal(HttpStatusCode.OK, patchResp.StatusCode);
        var patchedJson = await patchResp.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("target", patchedJson.GetProperty("status").GetString());
        Assert.Equal("Real-time streaming platform", patchedJson.GetProperty("description").GetString());
        Assert.Equal("Chicago, IL", patchedJson.GetProperty("headquarters").GetString());
        Assert.Equal("private", patchedJson.GetProperty("ownershipType").GetString());
        Assert.Equal(2, patchedJson.GetProperty("categoryTags").GetArrayLength());
        Assert.Equal(1, patchedJson.GetProperty("careerSources").GetArrayLength());
    }
}
