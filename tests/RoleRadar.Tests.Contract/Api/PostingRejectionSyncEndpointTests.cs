using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

/// <summary>
/// Feature 008 phase 2: the transition endpoint carries eventDate and warnings, and the application event
/// DTO exposes eventSource. Real Mongo (isolated Testcontainers), in-process API, fictional data only.
/// </summary>
public sealed class PostingRejectionSyncEndpointTests(SharedMongoFixture sharedMongo)
{
    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

    [Fact]
    public async Task Reject_WithEventDate_Returns200WithEmptyWarnings_AndTheEventCarriesDateAndSource()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_reject_sync_ok");
        var (postingId, applicationId, versionToken) = await SeedAppliedPostingAsync(host);

        var response = await host.Client.PostAsJsonAsync($"/api/v1/postings/{postingId}/transition", new
        {
            toDisposition = "rejected",
            reason = "Rejected by email",
            versionToken = versionToken.ToString(),
            eventDate = "2026-09-15"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rejected", detail.GetProperty("disposition").GetString());
        var warnings = detail.GetProperty("warnings");
        Assert.Equal(JsonValueKind.Array, warnings.ValueKind);
        Assert.Equal(0, warnings.GetArrayLength());

        var app = await host.Client.GetFromJsonAsync<JsonElement>($"/api/v1/applications/{applicationId}");
        var rejected = Assert.Single(
            app.GetProperty("events").EnumerateArray().ToList(),
            e => e.GetProperty("eventType").GetString() == "rejected");
        Assert.Equal("2026-09-15", rejected.GetProperty("eventDate").GetString());
        Assert.Equal("posting_transition", rejected.GetProperty("eventSource").GetString());
    }

    [Fact]
    public async Task Reject_WithMalformedEventDate_Returns400NamingTheEventDateField_AndChangesNothing()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_rej_bad_date");
        var (postingId, applicationId, versionToken) = await SeedAppliedPostingAsync(host);

        var response = await host.Client.PostAsJsonAsync($"/api/v1/postings/{postingId}/transition", new
        {
            toDisposition = "rejected",
            reason = "Rejected by email",
            versionToken = versionToken.ToString(),
            eventDate = "09/15/2026"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_error", error.GetProperty("errorCode").GetString());
        Assert.True(error.GetProperty("errors").TryGetProperty("eventDate", out var eventDateIssues) && eventDateIssues.GetArrayLength() >= 1);

        var posting = await host.Client.GetFromJsonAsync<JsonElement>($"/api/v1/postings/{postingId}");
        Assert.Equal("applied", posting.GetProperty("disposition").GetString());
        var app = await host.Client.GetFromJsonAsync<JsonElement>($"/api/v1/applications/{applicationId}");
        Assert.DoesNotContain(
            app.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "rejected");
    }

    [Fact]
    public async Task ErrorsAreUnchanged_404ForUnknownPosting_409ForStaleVersionToken()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_reject_sync_errors");
        var (postingId, _, _) = await SeedAppliedPostingAsync(host);

        var missing = await host.Client.PostAsJsonAsync($"/api/v1/postings/{Guid.CreateVersion7()}/transition", new
        {
            toDisposition = "rejected",
            reason = "Rejected by email",
            versionToken = Guid.CreateVersion7().ToString()
        });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var stale = await host.Client.PostAsJsonAsync($"/api/v1/postings/{postingId}/transition", new
        {
            toDisposition = "rejected",
            reason = "Rejected by email",
            versionToken = Guid.CreateVersion7().ToString()
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task GetPosting_WarningsIsNull_OnlyTheTransitionEndpointSetsIt()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_reject_sync_get");
        var (postingId, _, _) = await SeedAppliedPostingAsync(host);

        var detail = await host.Client.GetFromJsonAsync<JsonElement>($"/api/v1/postings/{postingId}");

        Assert.True(
            !detail.TryGetProperty("warnings", out var warnings) || warnings.ValueKind == JsonValueKind.Null,
            "GET must not carry warnings.");
    }

    [Fact]
    public async Task GetPosting_ReturnsTheApplicationId_ForAPostingWithAnApplication_AndNullForOneWithout()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_posting_app_id");
        var (postingId, applicationId, _) = await SeedAppliedPostingAsync(host);
        var bare = await SeedPostingWithoutApplicationAsync(host);

        var withApplication = await host.Client.GetFromJsonAsync<JsonElement>($"/api/v1/postings/{postingId}");
        Assert.Equal(applicationId, withApplication.GetProperty("applicationId").GetGuid());

        var withoutApplication = await host.Client.GetFromJsonAsync<JsonElement>($"/api/v1/postings/{bare}");
        Assert.True(withoutApplication.TryGetProperty("applicationId", out var none), "applicationId must be present.");
        Assert.Equal(JsonValueKind.Null, none.ValueKind);
    }

    [Fact]
    public async Task Transition_Response_CarriesTheSameApplicationId_AsTheDetailEndpoint()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_transition_app_id");
        var (postingId, applicationId, versionToken) = await SeedAppliedPostingAsync(host);

        var response = await host.Client.PostAsJsonAsync($"/api/v1/postings/{postingId}/transition", new
        {
            toDisposition = "rejected",
            reason = "Rejected by email",
            versionToken = versionToken.ToString()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(applicationId, detail.GetProperty("applicationId").GetGuid());
    }

    [Fact]
    public async Task Transition_Response_HasANullApplicationId_WhenThePostingHasNoApplication()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_trans_no_appid");
        // Another posting in the same store has an application; the lookup is per posting, so it must not leak.
        _ = await SeedAppliedPostingAsync(host);
        var bare = await SeedPostingWithoutApplicationAsync(host);
        var current = await host.Client.GetFromJsonAsync<JsonElement>($"/api/v1/postings/{bare}");

        var response = await host.Client.PostAsJsonAsync($"/api/v1/postings/{bare}/transition", new
        {
            toDisposition = "screened_out",
            reason = "Not a fit",
            versionToken = current.GetProperty("versionToken").GetString()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("applicationId").ValueKind);
    }

    [Fact]
    public async Task Reopen_RejectedPostingWithRejectedApplication_ReturnsTheStillRejectedWarning_WithItsMessage()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_reject_sync_reopen");
        var (postingId, applicationId, versionToken) = await SeedAppliedPostingAsync(host);

        var reject = await host.Client.PostAsJsonAsync($"/api/v1/postings/{postingId}/transition", new
        {
            toDisposition = "rejected",
            reason = "Rejected by email",
            versionToken = versionToken.ToString()
        });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);
        var rejectedDetail = await reject.Content.ReadFromJsonAsync<JsonElement>();

        var reopen = await host.Client.PostAsJsonAsync($"/api/v1/postings/{postingId}/transition", new
        {
            toDisposition = "lead",
            reason = "",
            versionToken = rejectedDetail.GetProperty("versionToken").GetString(),
            overrideReason = "Recruiter reopened the role"
        });

        Assert.Equal(HttpStatusCode.OK, reopen.StatusCode);
        var detail = await reopen.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("lead", detail.GetProperty("disposition").GetString());
        var warning = Assert.Single(detail.GetProperty("warnings").EnumerateArray().ToList());
        Assert.Equal("application_still_rejected", warning.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(warning.GetProperty("message").GetString()));

        var app = await host.Client.GetFromJsonAsync<JsonElement>($"/api/v1/applications/{applicationId}");
        Assert.Equal("rejected", app.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ApplicationDetail_Events_ExposeEventSource_ForManualEventsToo()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_reject_sync_source");
        var (_, applicationId, _) = await SeedAppliedPostingAsync(host);

        var append = await host.Client.PostAsJsonAsync($"/api/v1/applications/{applicationId}/events", new
        {
            eventType = "interview",
            eventDate = "2026-09-10",
            notes = "Phone screen",
            eventSource = "web"
        });
        Assert.Equal(HttpStatusCode.OK, append.StatusCode);

        var app = await host.Client.GetFromJsonAsync<JsonElement>($"/api/v1/applications/{applicationId}");
        var interview = Assert.Single(
            app.GetProperty("events").EnumerateArray().ToList(),
            e => e.GetProperty("eventType").GetString() == "interview");
        Assert.Equal("web", interview.GetProperty("eventSource").GetString());
    }

    private static async Task<Guid> SeedPostingWithoutApplicationAsync(ApiContractTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();

        var company = await companyHandler.HandleAsync(new RecordCompanyCommand(Name: $"Lantern Systems {Guid.NewGuid():N}", Actor: "system"));
        Assert.True(company.IsSuccess, company.Error?.Message);
        var posting = await postingHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: company.Value!.Company.Id,
            Title: "Platform Engineer",
            RequisitionId: $"LAN-{Guid.NewGuid():N}"[..12],
            AtsTenant: "lantern",
            Actor: "mcp-stdio"));
        Assert.True(posting.IsSuccess, posting.Error?.Message);
        return posting.Value!.Posting!.Id;
    }

    private static async Task<(Guid PostingId, Guid ApplicationId, Guid VersionToken)> SeedAppliedPostingAsync(ApiContractTestHost host)
    {
        using var scope = host.Services.CreateScope();
        var companyHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordCompanyCommand, CompanyRecordResultDto>>();
        var postingHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecordPostingCommand, PostingRecordResultDto>>();

        var company = await companyHandler.HandleAsync(new RecordCompanyCommand(Name: $"Harbor Analytics {Guid.NewGuid():N}", Actor: "system"));
        Assert.True(company.IsSuccess, company.Error?.Message);
        var posting = await postingHandler.HandleAsync(new RecordPostingCommand(
            CompanyId: company.Value!.Company.Id,
            Title: "Staff Platform Engineer",
            RequisitionId: $"HAR-{Guid.NewGuid():N}"[..12],
            AtsTenant: "harbor",
            Actor: "mcp-stdio"));
        Assert.True(posting.IsSuccess, posting.Error?.Message);
        var postingId = posting.Value!.Posting!.Id;

        var create = await host.Client.PostAsJsonAsync("/api/v1/applications", new
        {
            postingId,
            submittedDate = "2026-09-01",
            resumeVariant = "tailored"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var application = await create.Content.ReadFromJsonAsync<JsonElement>();

        var current = await host.Client.GetFromJsonAsync<JsonElement>($"/api/v1/postings/{postingId}");
        Assert.Equal("applied", current.GetProperty("disposition").GetString());
        return (postingId, application.GetProperty("id").GetGuid(), Guid.Parse(current.GetProperty("versionToken").GetString()!));
    }
}
