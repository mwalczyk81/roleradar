using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

public sealed class PostingsEndpointsContractTests(SharedMongoFixture sharedMongo)
{
    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

    [Fact]
    public async Task GetPostings_EnforcesBoundedPagination_Default50_Max200_AndFilters()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_postings_page");
        using var scope = host.Services.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();

        var companyRes = await companyHandler.HandleAsync(new RecordCompanyCommand(Name: "Acme Cloud Corp"));
        Assert.True(companyRes.IsSuccess);
        var companyId = companyRes.Value!.Company.Id;

        for (var i = 1; i <= 5; i++)
        {
            var pRes = await postingHandler.HandleAsync(new RecordPostingCommand(
                CompanyId: companyId,
                Title: $"Staff Platform Engineer {i}",
                RequisitionId: $"REQ-{i:D3}",
                AtsTenant: "acme",
                Location: "Remote - US",
                Score: 8.5m));
            Assert.True(pRes.IsSuccess);
        }

        // Default paging (limit omitted -> 50, offset omitted -> 0)
        var defaultResp = await host.Client.GetAsync("/api/v1/postings");
        Assert.Equal(HttpStatusCode.OK, defaultResp.StatusCode);

        var defaultJson = await defaultResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, defaultJson.GetProperty("totalCount").GetInt32());
        Assert.Equal(50, defaultJson.GetProperty("limit").GetInt32());
        Assert.Equal(0, defaultJson.GetProperty("offset").GetInt32());
        Assert.Equal(5, defaultJson.GetProperty("items").GetArrayLength());

        // Clamp limit > 200 down to 200
        var clampedResp = await host.Client.GetAsync("/api/v1/postings?limit=500&offset=2");
        Assert.Equal(HttpStatusCode.OK, clampedResp.StatusCode);

        var clampedJson = await clampedResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(200, clampedJson.GetProperty("limit").GetInt32());
        Assert.Equal(2, clampedJson.GetProperty("offset").GetInt32());
        Assert.Equal(3, clampedJson.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task GetPostingById_Returns200WithDetail_Or404WithErrorEnvelope()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_posting_detail");
        using var scope = host.Services.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();

        var companyRes = await companyHandler.HandleAsync(new RecordCompanyCommand(Name: "Nimbus Systems"));
        var companyId = companyRes.Value!.Company.Id;

        var postingRes = await postingHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: companyId,
            Title: "Principal Distributed Systems Engineer",
            Url: "https://boards.greenhouse.io/nimbus/jobs/101",
            RequisitionId: "NIM-101",
            AtsTenant: "nimbus",
            Location: "Remote",
            CompensationText: "$210k - $240k USD",
            Score: 9.0m));
        var postingId = postingRes.Value!.Posting!.Id;

        var getResp = await host.Client.GetAsync($"/api/v1/postings/{postingId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var detail = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(postingId, detail.GetProperty("id").GetGuid());
        Assert.Equal("Nimbus Systems", detail.GetProperty("companyName").GetString());
        Assert.Equal("Principal Distributed Systems Engineer", detail.GetProperty("title").GetString());

        // Non-existent posting -> 404 with ErrorEnvelope
        var missingResp = await host.Client.GetAsync($"/api/v1/postings/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.NotFound, missingResp.StatusCode);
        var err = await missingResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(err.GetProperty("ok").GetBoolean());
        Assert.Equal("not_found", err.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task TransitionPosting_UpdatesDisposition_AndReturns409ConflictOnStaleVersionToken()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_posting_transition");
        using var scope = host.Services.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();

        var companyRes = await companyHandler.HandleAsync(new RecordCompanyCommand(Name: "Vertex Analytics"));
        var postingRes = await postingHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: companyRes.Value!.Company.Id,
            Title: "Senior Backend Engineer",
            RequisitionId: "VTX-202",
            AtsTenant: "vertex"));

        var postingId = postingRes.Value!.Posting!.Id;
        var initialVersionToken = postingRes.Value.Posting.VersionToken;

        // 1. Valid transition with current versionToken -> 200 OK
        var validPayload = new
        {
            toDisposition = "screened_out",
            reason = "Requires hybrid 4 days in office",
            versionToken = initialVersionToken.ToString()
        };

        var transitionResp = await host.Client.PostAsJsonAsync($"/api/v1/postings/{postingId}/transition", validPayload);
        Assert.Equal(HttpStatusCode.OK, transitionResp.StatusCode);
        var updated = await transitionResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("screened_out", updated.GetProperty("disposition").GetString());
        var newVersionToken = updated.GetProperty("versionToken").GetGuid();
        Assert.NotEqual(initialVersionToken, newVersionToken);

        // 2. Stale versionToken -> 409 Conflict with concurrency_conflict errorCode
        var staleResp = await host.Client.PostAsJsonAsync($"/api/v1/postings/{postingId}/transition", validPayload);
        Assert.Equal(HttpStatusCode.Conflict, staleResp.StatusCode);
        var conflictErr = await staleResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(conflictErr.GetProperty("ok").GetBoolean());
        Assert.Equal("concurrency_conflict", conflictErr.GetProperty("errorCode").GetString());
    }
}
