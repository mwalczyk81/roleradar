using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

/// <summary>
/// Feature 014 US2 (013 lesson, research R-007): the problem-details registration, the exception handler and the OpenAPI schema
/// transformer write nothing at warning level or above while the API starts with no Mongo. That is exactly the build-time
/// run (<c>dotnet-getdocument</c> under the entry assembly <c>GetDocument.Insider</c>), where stderr output fails the build
/// (MSB3077). The test also generates the document, because the schema transformer only runs then.
/// </summary>
public sealed class ApiStartupQuietTests
{
    // Nothing listens on port 1. Never the developer's Mongo (27017).
    private const string UnreachableMongo = "mongodb://127.0.0.1:1/?directConnection=true";

    [Fact]
    public async Task StartingAndGeneratingTheDocument_WithNoMongo_WritesNothingAtWarnOrAbove()
    {
        using var capture = new FormatterLogCapture(RoleRadarConsoleFormatter.Create("api", LogFormat.Json));
        await using var app = RoleRadar.Api.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0", "--environment", "Development"],
            builder =>
            {
                builder.Logging.AddProvider(capture);
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = UnreachableMongo,
                    ["DatabaseName"] = "rrtest_problem_startup_quiet",
                });
            },
            entryAssemblyName: "GetDocument.Insider");

        await app.StartAsync(TestContext.Current.CancellationToken);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var document = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Contains("\"ProblemDetails\"", await document.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        await app.StopAsync(TestContext.Current.CancellationToken);

        // Non-vacuous: the host did log (Information startup records), and none is a warning or worse.
        Assert.NotEmpty(capture.Lines);
        var loud = capture.Lines
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Where(record => record.GetProperty("level").GetString() is "warn" or "error" or "fatal")
            .Select(record => record.GetProperty("msg").GetString())
            .ToList();
        Assert.Empty(loud);
    }
}
