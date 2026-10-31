using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace RoleRadar.Tests.Contract.OpenApi;

public sealed class ApiOpenApiContractSnapshotTests
{
    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] ExpectedPaths =
    [
        "/health",
        "/api/v1/postings",
        "/api/v1/postings/{id}",
        "/api/v1/postings/{id}/transition",
        "/api/v1/companies",
        "/api/v1/companies/{id}",
        "/api/v1/applications",
        "/api/v1/applications/{id}",
        "/api/v1/applications/{id}/events",
        "/api/v1/criteria",
        "/api/v1/notes",
        "/api/v1/notes/{id}",
        "/api/v1/search-runs"
    ];

    [Fact]
    public async Task OpenApiDocument_MatchesCommittedSnapshot_And_HasExplicitTypesOnAllParametersAndProperties()
    {
        var repoRoot = FindRepoRoot();
        var snapshotPath = Path.Combine(repoRoot, "contracts", "openapi", "roleradar-api.json");

        Assert.True(
            File.Exists(snapshotPath),
            $"Expected committed OpenAPI specification at '{snapshotPath}'. Run 'dotnet build src/RoleRadar.Api' to generate it.");

        var snapshotText = await File.ReadAllTextAsync(snapshotPath);
        using var snapshotDoc = JsonDocument.Parse(snapshotText);
        var snapshotRoot = snapshotDoc.RootElement;

        Assert.Equal("3.1.1", snapshotRoot.GetProperty("openapi").GetString());

        // 1. Verify all expected paths exist in the committed snapshot
        var paths = snapshotRoot.GetProperty("paths");
        foreach (var expectedPath in ExpectedPaths)
        {
            Assert.True(
                paths.TryGetProperty(expectedPath, out _),
                $"Expected path '{expectedPath}' in OpenAPI specification.");
        }

        // 2. Verify every parameter and component schema property declares an explicit scalar type (no untyped or union ["integer","string"] schemas)
        AssertExplicitTypesInPaths(paths);

        var schemas = snapshotRoot.GetProperty("components").GetProperty("schemas");
        AssertExplicitTypesInComponentSchemas(schemas);

        // 3. Compare live runtime OpenAPI endpoint (/openapi/v1.json) against the committed snapshot in Development
        await using var app = RoleRadar.Api.Program.CreateApp(["--urls", "http://127.0.0.1:0", "--environment", "Development"]);
        await app.StartAsync();

        var server = app.Services.GetRequiredService<IServer>();
        var baseAddress = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();

        using var client = new HttpClient { BaseAddress = new Uri(baseAddress) };
        var liveResp = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, liveResp.StatusCode);

        var liveText = await liveResp.Content.ReadAsStringAsync();
        using var liveDoc = JsonDocument.Parse(liveText);

        var normalizedSnapshot = NormalizeJson(snapshotRoot);
        var normalizedLive = NormalizeJson(liveDoc.RootElement);

        Assert.Equal(normalizedSnapshot, normalizedLive);

        // Verify /openapi/v1.json is NOT exposed in Production
        await using var prodApp = RoleRadar.Api.Program.CreateApp(["--urls", "http://127.0.0.1:0", "--environment", "Production"]);
        await prodApp.StartAsync();
        var prodServer = prodApp.Services.GetRequiredService<IServer>();
        var prodBaseAddress = prodServer.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var prodClient = new HttpClient { BaseAddress = new Uri(prodBaseAddress) };
        var prodResp = await prodClient.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, prodResp.StatusCode);
        await prodApp.StopAsync();

        // 4. Verify generated TypeScript client exists and is non-empty
        var tsClientPath = Path.Combine(repoRoot, "contracts", "client", "api-types.ts");
        Assert.True(
            File.Exists(tsClientPath),
            $"Expected generated TypeScript client types at '{tsClientPath}'.");
        var tsContent = await File.ReadAllTextAsync(tsClientPath);
        Assert.Contains("export interface paths", tsContent, StringComparison.Ordinal);
        Assert.Contains("/api/v1/postings", tsContent, StringComparison.Ordinal);

        await app.StopAsync();
    }

    private static void AssertExplicitTypesInPaths(JsonElement paths)
    {
        foreach (var pathProp in paths.EnumerateObject())
        {
            foreach (var operationProp in pathProp.Value.EnumerateObject())
            {
                if (!operationProp.Value.TryGetProperty("parameters", out var parameters) ||
                    parameters.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var param in parameters.EnumerateArray())
                {
                    var paramName = param.GetProperty("name").GetString();
                    Assert.True(
                        param.TryGetProperty("schema", out var schema),
                        $"Parameter '{paramName}' in '{operationProp.Name.ToUpperInvariant()} {pathProp.Name}' is missing a schema.");

                    AssertHasExplicitScalarType(
                        schema,
                        $"parameter '{paramName}' in '{operationProp.Name.ToUpperInvariant()} {pathProp.Name}'");
                }
            }
        }
    }

    private static void AssertExplicitTypesInComponentSchemas(JsonElement schemas)
    {
        foreach (var schemaProp in schemas.EnumerateObject())
        {
            var schemaName = schemaProp.Name;
            var schemaObj = schemaProp.Value;

            if (!schemaObj.TryGetProperty("properties", out var properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var prop in properties.EnumerateObject())
            {
                AssertHasExplicitScalarType(
                    prop.Value,
                    $"property '{schemaName}.{prop.Name}'");
            }
        }
    }

    private static void AssertHasExplicitScalarType(JsonElement schema, string context)
    {
        if (schema.TryGetProperty("$ref", out _))
        {
            return;
        }

        if (schema.TryGetProperty("oneOf", out var oneOf) && oneOf.ValueKind == JsonValueKind.Array)
        {
            foreach (var branch in oneOf.EnumerateArray())
            {
                AssertHasExplicitScalarType(branch, context);
            }
            return;
        }

        if (schema.TryGetProperty("anyOf", out var anyOf) && anyOf.ValueKind == JsonValueKind.Array)
        {
            foreach (var branch in anyOf.EnumerateArray())
            {
                AssertHasExplicitScalarType(branch, context);
            }
            return;
        }

        Assert.True(
            schema.TryGetProperty("type", out var typeProp),
            $"Expected explicit 'type' on {context}, but got: {schema.GetRawText()}");

        if (typeProp.ValueKind == JsonValueKind.String)
        {
            Assert.False(string.IsNullOrWhiteSpace(typeProp.GetString()), $"Empty 'type' on {context}.");
            return;
        }

        if (typeProp.ValueKind == JsonValueKind.Array)
        {
            var types = typeProp.EnumerateArray().Select(e => e.GetString()).Where(s => s is not null).ToArray();
            var nonNullTypes = types.Where(t => t != "null").ToArray();
            Assert.True(
                nonNullTypes.Length == 1,
                $"Expected a single explicit scalar type (with optional 'null') on {context}, but found ambiguous union [{string.Join(", ", types)}].");
            return;
        }

        Assert.Fail($"Unexpected 'type' kind {typeProp.ValueKind} on {context}.");
    }

    private static string NormalizeJson(JsonElement element)
    {
        return JsonSerializer.Serialize(element, IndentedJsonOptions);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate RoleRadar.sln from test base directory.");
    }
}
