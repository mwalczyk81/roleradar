using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

public sealed class AncillaryEndpointsContractTests(SharedMongoFixture sharedMongo)
{
    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

    [Fact]
    public async Task GetCriteria_Notes_AndSearchRuns_ReturnExpectedDtos()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_ancillary");
        using var scope = host.Services.CreateScope();
        var trackerLogHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<TrackerLogCommand, TrackerLogResultDto>>();
        var createNoteHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateNoteCommand, NoteCreatedDto>>();

        // Seed a criterion
        var critRes = await trackerLogHandler.HandleAsync(new TrackerLogCommand(
            Action: "criterion_revision",
            Key: "remote_first",
            Category: "preference",
            Value: "Prefer fully remote distributed engineering teams",
            Provenance: "stated",
            EffectiveFrom: "2026-10-01T00:00:00Z"));
        Assert.True(critRes.IsSuccess);

        // Seed a search run
        var srRes = await trackerLogHandler.HandleAsync(new TrackerLogCommand(
            Action: "search_run",
            StartedAt: "2026-10-25T15:00:00Z",
            NewPostingsCount: 7,
            Notes: "Scanned 12 ATS boards"));
        Assert.True(srRes.IsSuccess);

        // Seed a note
        var noteRes = await createNoteHandler.HandleAsync(new CreateNoteCommand(
            Title: "Architecture Interview Prep",
            Body: "Review distributed consensus and event sourcing trade-offs.",
            Tags: ["interview", "architecture"],
            Area: "prep",
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-25",
            ProvenanceQualifier: null));
        Assert.True(noteRes.IsSuccess);
        var noteId = noteRes.Value!.Id;

        // 1. GET /api/v1/criteria
        var critResp = await host.Client.GetAsync("/api/v1/criteria");
        Assert.Equal(HttpStatusCode.OK, critResp.StatusCode);
        var critJson = await critResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, critJson.ValueKind);
        Assert.Equal(1, critJson.GetArrayLength());
        Assert.Equal("remote_first", critJson[0].GetProperty("name").GetString());

        // 2. GET /api/v1/notes/{id}
        var noteGetResp = await host.Client.GetAsync($"/api/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.OK, noteGetResp.StatusCode);
        var noteJson = await noteGetResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(noteId, noteJson.GetProperty("id").GetGuid());
        Assert.Equal("Architecture Interview Prep", noteJson.GetProperty("heading").GetString());

        // 3. GET /api/v1/notes?q=consensus
        var noteSearchResp = await host.Client.GetAsync("/api/v1/notes?q=consensus&limit=10&offset=0");
        Assert.Equal(HttpStatusCode.OK, noteSearchResp.StatusCode);
        var noteSearchJson = await noteSearchResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(noteSearchJson.GetProperty("totalCount").GetInt32() >= 1);

        // 4. GET /api/v1/search-runs
        var srResp = await host.Client.GetAsync("/api/v1/search-runs?limit=10&offset=0");
        Assert.Equal(HttpStatusCode.OK, srResp.StatusCode);
        var srJson = await srResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, srJson.GetProperty("totalCount").GetInt32());
        Assert.Equal(7, srJson.GetProperty("items")[0].GetProperty("newPostings").GetInt32());
    }
}
