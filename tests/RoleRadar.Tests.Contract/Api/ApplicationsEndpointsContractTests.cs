using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

public sealed class ApplicationsEndpointsContractTests(SharedMongoFixture sharedMongo)
{
    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

    [Fact]
    public async Task CreateApplication_AppendEvent_AndQueryApplications_Succeed()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_applications");
        using var scope = host.Services.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();

        var companyRes = await companyHandler.HandleAsync(new RecordCompanyCommand(Name: "stratos-tech"));
        var postingRes = await postingHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: companyRes.Value!.Company.Id,
            Title: "Lead .NET Architect",
            RequisitionId: "STR-900",
            AtsTenant: "stratos"));

        var postingId = postingRes.Value!.Posting!.Id;

        // 1. POST /api/v1/applications -> 201 Created
        var createPayload = new
        {
            postingId = postingId,
            submittedDate = "2026-10-25",
            resumeVariant = "tailored",
            desiredSalaryMin = 190000m,
            desiredSalaryMax = 220000m,
            desiredSalaryCurrency = "USD"
        };

        var createResp = await host.Client.PostAsJsonAsync("/api/v1/applications", createPayload);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var createdApp = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var applicationId = createdApp.GetProperty("id").GetGuid();
        Assert.Equal(postingId, createdApp.GetProperty("postingId").GetGuid());
        Assert.Equal(1, createdApp.GetProperty("events").GetArrayLength());

        // 2. POST /api/v1/applications/{id}/events -> 200 OK
        var eventPayload = new
        {
            eventType = "interview",
            eventDate = "2026-10-26",
            notes = "Completed system design interview"
        };

        var eventResp = await host.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/events", eventPayload);
        Assert.Equal(HttpStatusCode.OK, eventResp.StatusCode);
        var updatedApp = await eventResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, updatedApp.GetProperty("events").GetArrayLength());

        // 3. GET /api/v1/applications/{id} -> 200 OK
        var getResp = await host.Client.GetAsync($"/api/v1/applications/{applicationId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var fetchedApp = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(applicationId, fetchedApp.GetProperty("id").GetGuid());
        Assert.Equal(2, fetchedApp.GetProperty("events").GetArrayLength());

        // 4. GET /api/v1/applications -> 200 OK with paged items
        var listResp = await host.Client.GetAsync("/api/v1/applications?limit=25&offset=0");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listJson = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, listJson.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, listJson.GetProperty("items").GetArrayLength());
    }
}
