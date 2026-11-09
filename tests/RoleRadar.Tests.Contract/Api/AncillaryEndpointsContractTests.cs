using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Notes.Commands;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

public sealed class AncillaryEndpointsContractTests(SharedMongoFixture sharedMongo)
{
    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

    [Fact]
    public async Task GetNoteById_ReturnsExpectedDto()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_ancillary");
        using var scope = host.Services.CreateScope();
        var createNoteHandler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateNoteCommand, NoteCreatedDto>>();

        // Seed a note
        var noteRes = await createNoteHandler.HandleAsync(new CreateNoteCommand(
            Title: "Architecture Interview Prep",
            Body: "Review distributed consensus and event sourcing trade-offs.",
            Tags: ["interview", "architecture"],
            Area: "prep",
            ProvenanceKind: "stated",
            ProvenanceDate: "2026-10-25",
            ProvenanceQualifier: null,
            Actor: "mcp-stdio"));
        Assert.True(noteRes.IsSuccess);
        var noteId = noteRes.Value!.Id;

        // GET /api/v1/notes/{id}
        var noteGetResp = await host.Client.GetAsync($"/api/v1/notes/{noteId}");
        Assert.Equal(HttpStatusCode.OK, noteGetResp.StatusCode);
        var noteJson = await noteGetResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(noteId, noteJson.GetProperty("id").GetGuid());
        Assert.Equal("Architecture Interview Prep", noteJson.GetProperty("heading").GetString());
    }
}
