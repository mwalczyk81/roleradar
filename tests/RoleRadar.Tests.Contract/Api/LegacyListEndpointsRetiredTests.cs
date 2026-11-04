using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

/// <summary>
/// T066: the six list reads moved to /odata/*, so the legacy collection GETs must be gone. Detail GETs and the
/// command endpoints that share a path (for example POST /api/v1/applications) are untouched.
/// </summary>
public sealed class LegacyListEndpointsRetiredTests(SharedMongoFixture sharedMongo)
{
    public static TheoryData<string> LegacyListPaths =>
    [
        "/api/v1/postings",
        "/api/v1/companies",
        "/api/v1/applications",
        "/api/v1/notes?q=anything",
        "/api/v1/criteria",
        "/api/v1/search-runs"
    ];

    [Theory]
    [MemberData(nameof(LegacyListPaths))]
    public async Task LegacyListGet_IsRetired(string path)
    {
        await using var host = await ApiContractTestHost.StartAsync(sharedMongo, "rrtest_api_retired");

        using var response = await host.Client.GetAsync(path);

        // 404 (no route) or 405 (the path still exists for a POST); never data.
        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"GET {path} returned {(int)response.StatusCode}; the legacy list endpoint must be retired.");
    }

    [Fact]
    public async Task DetailAndCommandRoutes_StillExist()
    {
        await using var host = await ApiContractTestHost.StartAsync(sharedMongo, "rrtest_api_retired_kept");
        var missing = Guid.NewGuid();

        // Unknown id: the route exists and answers 404 from the handler (an envelope), not from routing.
        foreach (var path in new[] { "postings", "companies", "applications", "notes" })
        {
            using var response = await host.Client.GetAsync($"/api/v1/{path}/{missing}");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("not_found", body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
