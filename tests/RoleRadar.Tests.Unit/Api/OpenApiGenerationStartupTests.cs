using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RoleRadar.Api.OData;
using RoleRadar.Api.OpenApi;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Unit.Api;

/// <summary>
/// The API builds its OpenAPI document at build time: <c>dotnet-getdocument</c> (Microsoft.Extensions.ApiDescription.Server)
/// runs the API's entry point under the entry assembly name <c>GetDocument.Insider</c>, with a mock server. Everything that
/// startup does runs, including hosted services. Since feature 013 sends warn and above to stderr, a startup service that
/// cannot reach Mongo and logs an error makes MSBuild's Exec task fail the build (MSB3077), on any machine with no Mongo, such
/// as CI. During generation the API therefore starts no hosted service and writes nothing at warn or above.
/// The guard follows Microsoft's guidance for build-time generation: check the entry assembly name.
/// </summary>
public sealed class OpenApiGenerationStartupTests
{
    private const string Generator = "GetDocument.Insider";

    // Nothing listens on port 1. Never the developer's Mongo (27017).
    private const string UnreachableMongo = "mongodb://127.0.0.1:1/?directConnection=true";

    [Theory]
    [InlineData("GetDocument.Insider", true)]
    [InlineData("RoleRadar.Api", false)]
    [InlineData("testhost", false)]
    [InlineData("getdocument.insider", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TheGeneratorIsRecognisedByItsEntryAssemblyName(string? entryAssemblyName, bool expected) =>
        Assert.Equal(expected, OpenApiDocumentGeneration.IsGeneratingDocument(entryAssemblyName));

    [Fact]
    public void ARunningTestProcess_IsNotTheGenerator() =>
        Assert.False(OpenApiDocumentGeneration.IsGeneratingDocument());

    private static WebApplication Create(string entryAssemblyName, FormatterLogCapture capture) =>
        RoleRadar.Api.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Logging.AddProvider(capture);
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = UnreachableMongo,
                    ["DatabaseName"] = "rrtest_openapi_generation",
                });
            },
            entryAssemblyName);

    private static FormatterLogCapture NewCapture() => new(RoleRadarConsoleFormatter.Create("api", LogFormat.Json));

    private static List<string> WarnOrAbove(FormatterLogCapture capture) =>
        capture.Lines
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Where(record => record.GetProperty("level").GetString() is "warn" or "error" or "fatal")
            .Select(record => record.GetProperty("msg").GetString() ?? string.Empty)
            .ToList();

    [Fact]
    public async Task AsAnOrdinaryApi_TheReadIndexServiceIsRegistered()
    {
        await using var app = Create("RoleRadar.Api", NewCapture());

        Assert.Contains(app.Services.GetServices<IHostedService>(), service => service is ReadIndexStartupService);
    }

    [Fact]
    public async Task WhileGeneratingTheDocument_OnlyTheWebHostItselfIsStarted()
    {
        await using var app = Create(Generator, NewCapture());

        // The generator needs the web host (it swaps in a mock server). Everything the application and its libraries added is
        // gone: the read-index service, the data-protection key ring, the health-check publisher.
        var remaining = app.Services.GetServices<IHostedService>().Select(service => service.GetType().FullName).ToList();

        Assert.Equal(["Microsoft.AspNetCore.Hosting.GenericWebHostService"], remaining);
    }

    [Fact]
    public async Task AsAnOrdinaryApi_AnUnreachableMongo_IsLoggedAtError()
    {
        // The control for the next test: this is the record that failed the build, so the capture and the scenario are real.
        var capture = NewCapture();
        await using var app = Create("RoleRadar.Api", capture);

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.Contains(WarnOrAbove(capture), message => message.Contains("read indexes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhileGeneratingTheDocument_StartingWithNoMongo_WritesNothingAtWarnOrAbove()
    {
        var capture = NewCapture();
        await using var app = Create(Generator, capture);

        await app.StartAsync(TestContext.Current.CancellationToken);
        await app.StopAsync(TestContext.Current.CancellationToken);

        Assert.Empty(WarnOrAbove(capture));
    }
}
