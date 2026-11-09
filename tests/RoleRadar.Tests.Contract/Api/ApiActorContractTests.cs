using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Infrastructure.Mongo;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

/// <summary>
/// Feature 013 (FR-015, FR-017, SC-004): every API write names the signed-in subject as its audit actor, never a
/// placeholder, and the audit entry carries the request's correlation id.
/// </summary>
public sealed class ApiActorContractTests(SharedMongoFixture sharedMongo)
{
    // Fictional UUIDv7 subjects. The host allows the first one; the second is added here so a constant cannot pass.
    private const string OtherOwnerSubject = "0192e4a1-0000-7000-8000-0000000000a2";
    private static readonly string[] Placeholders = ["api-user", "agent", "system", "mcp-stdio"];

    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

    private async Task<ApiContractTestHost> StartHostAsync(string prefix)
    {
        var host = await ApiContractTestHost.StartAsync(
            _sharedMongo,
            prefix,
            builder => builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Owner:AllowedSubjects:1"] = OtherOwnerSubject
            }));

        host.Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", host.CreateValidOwnerToken(OtherOwnerSubject));
        return host;
    }

    private async Task<BsonDocument> NewestAuditEntryAsync(ApiContractTestHost host)
    {
        var client = new MongoClient(_sharedMongo.ConnectionString);
        var audit = client.GetDatabase(host.DatabaseName).GetCollection<BsonDocument>(MongoBootstrapAndIndexes.AuditLogCollectionName);
        return await audit.Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(Builders<BsonDocument>.Sort.Descending("occurred_at").Descending("_id"))
            .FirstAsync();
    }

    private static void AssertActorIsSubject(BsonDocument entry)
    {
        var actor = entry.GetValue("actor", "").AsString;
        Assert.Equal(OtherOwnerSubject, actor);
        Assert.DoesNotContain(actor, Placeholders);
    }

    private static async Task<(Guid CompanyId, Guid PostingId, Guid PostingVersion)> SeedAsync(ApiContractTestHost host, string name)
    {
        using var scope = host.Services.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();

        var company = await companyHandler.HandleAsync(new RecordCompanyCommand(Name: name, Actor: "contract-seed"));
        Assert.True(company.IsSuccess);
        var posting = await postingHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: company.Value!.Company.Id,
            Title: "Senior Backend Engineer",
            RequisitionId: "ACT-100",
            AtsTenant: "actor-tenant",
            Actor: "contract-seed"));
        Assert.True(posting.IsSuccess);
        return (company.Value.Company.Id, posting.Value!.Posting!.Id, posting.Value.Posting.VersionToken);
    }

    [Fact]
    public async Task CreateApplication_WritesAuditEntryNamingTheSubject()
    {
        await using var host = await StartHostAsync("rrtest_api_actor_app_create");
        var seed = await SeedAsync(host, "Actor Create Labs");

        var response = await host.Client.PostAsJsonAsync(
            "/api/v1/applications",
            new { postingId = seed.PostingId, submittedDate = "2026-10-25", resumeVariant = "tailored" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        AssertActorIsSubject(await NewestAuditEntryAsync(host));
    }

    [Fact]
    public async Task AppendApplicationEvent_WritesAuditEntryNamingTheSubject()
    {
        await using var host = await StartHostAsync("rrtest_api_actor_app_event");
        var seed = await SeedAsync(host, "Actor Event Labs");
        var created = await host.Client.PostAsJsonAsync("/api/v1/applications", new { postingId = seed.PostingId, submittedDate = "2026-10-25" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var applicationId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await host.Client.PostAsJsonAsync(
            $"/api/v1/applications/{applicationId}/events",
            new { eventType = "interview", eventDate = "2026-10-26", notes = "Fictional interview" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertActorIsSubject(await NewestAuditEntryAsync(host));
    }

    [Fact]
    public async Task PatchCompany_WritesAuditEntryNamingTheSubject()
    {
        await using var host = await StartHostAsync("rrtest_api_actor_company");
        var seed = await SeedAsync(host, "Actor Company Labs");

        var response = await host.Client.PatchAsJsonAsync($"/api/v1/companies/{seed.CompanyId}", new { status = "target" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertActorIsSubject(await NewestAuditEntryAsync(host));
    }

    [Fact]
    public async Task TransitionPosting_WritesAuditEntryNamingTheSubject()
    {
        await using var host = await StartHostAsync("rrtest_api_actor_transition");
        var seed = await SeedAsync(host, "Actor Transition Labs");

        var response = await host.Client.PostAsJsonAsync(
            $"/api/v1/postings/{seed.PostingId}/transition",
            new { toDisposition = "screened_out", reason = "Fictional reason", versionToken = seed.PostingVersion.ToString() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertActorIsSubject(await NewestAuditEntryAsync(host));
    }

    [Fact]
    public async Task ApiWrite_WithCorrelationHeader_StoresItOnTheAuditEntry()
    {
        await using var host = await StartHostAsync("rrtest_api_actor_corr");
        var seed = await SeedAsync(host, "Actor Correlation Labs");

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/companies/{seed.CompanyId}")
        {
            Content = JsonContent.Create(new { status = "target" })
        };
        request.Headers.Add("X-Correlation-ID", "test-corr-0002");
        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await NewestAuditEntryAsync(host);
        Assert.True(entry.TryGetValue("request_correlation_id", out var stored), "audit entry has no request_correlation_id");
        Assert.Equal("test-corr-0002", stored!.AsString);
    }
}
