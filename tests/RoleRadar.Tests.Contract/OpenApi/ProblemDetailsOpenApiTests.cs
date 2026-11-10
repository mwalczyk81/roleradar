using System.Text.Json;
using Xunit;

namespace RoleRadar.Tests.Contract.OpenApi;

/// <summary>
/// Feature 014 US2 (FR-022, contracts/problem-details.md, research R-007): the committed OpenAPI document describes errors as
/// RFC 9457 problem documents. The envelope schemas are gone, <c>ProblemDetails</c> declares the typed string members
/// <c>errorCode</c> and <c>traceId</c> so the generated client needs no cast, and every error status an operation can return
/// is documented as <c>application/problem+json</c>.
/// </summary>
public sealed class ProblemDetailsOpenApiTests
{
    private static JsonElement Load()
    {
        var path = Path.Combine(FindRepoRoot(), "contracts", "openapi", "roleradar-api.json");
        Assert.True(File.Exists(path), $"Expected the committed OpenAPI document at '{path}'.");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    }

    private static IEnumerable<(string Path, string Method, JsonElement Operation)> Operations(JsonElement root)
    {
        foreach (var path in root.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Value.ValueKind == JsonValueKind.Object && operation.Value.TryGetProperty("responses", out _))
                {
                    yield return (path.Name, operation.Name, operation.Value);
                }
            }
        }
    }

    private static List<string> ContentTypes(JsonElement response) =>
        response.TryGetProperty("content", out var content)
            ? content.EnumerateObject().Select(c => c.Name).ToList()
            : [];

    [Fact]
    public void NoEnvelopeSchemaRemains()
    {
        var schemas = Load().GetProperty("components").GetProperty("schemas");

        Assert.False(schemas.TryGetProperty("ErrorEnvelope", out _), "ErrorEnvelope is still in the OpenAPI document.");
        Assert.False(schemas.TryGetProperty("ErrorFieldDetail", out _), "ErrorFieldDetail is still in the OpenAPI document.");
    }

    [Theory]
    [InlineData("ProblemDetails")]
    [InlineData("HttpValidationProblemDetails")]
    public void TheProblemSchemas_DeclareErrorCodeAndTraceId_AsStrings(string schemaName)
    {
        var schemas = Load().GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty(schemaName, out var schema), $"Schema '{schemaName}' is missing.");
        var properties = schema.GetProperty("properties");
        foreach (var member in new[] { "errorCode", "traceId" })
        {
            Assert.True(properties.TryGetProperty(member, out var property), $"{schemaName}.{member} is missing.");
            Assert.True(TypeIncludesOnlyString(property), $"{schemaName}.{member} must be a plain string.");
        }
    }

    [Fact]
    public void EveryErrorStatus_OfEveryOperation_IsDocumentedAsProblemJson_AndNeverAsJson()
    {
        var checkedStatuses = 0;
        foreach (var (path, method, operation) in Operations(Load()))
        {
            foreach (var response in operation.GetProperty("responses").EnumerateObject())
            {
                if (!int.TryParse(response.Name, out var status) || status < 400)
                {
                    continue;
                }

                var types = ContentTypes(response.Value).ToList();
                Assert.True(types.Contains("application/problem+json"), $"{method.ToUpperInvariant()} {path} {status} does not document application/problem+json (has: {string.Join(", ", types)}).");
                Assert.DoesNotContain("application/json", types);
                checkedStatuses++;
            }
        }

        Assert.True(checkedStatuses >= 10, $"Only {checkedStatuses} error responses were checked.");
    }

    [Theory]
    [InlineData("/api/v1/postings/{id}", "get", new[] { "401", "403", "404" })]
    [InlineData("/api/v1/postings/{id}/transition", "post", new[] { "400", "401", "403", "404", "409" })]
    [InlineData("/api/v1/companies/{id}", "patch", new[] { "400", "401", "403", "404", "409" })]
    [InlineData("/api/v1/applications", "post", new[] { "400", "401", "403" })]
    public void TheOperations_DocumentTheErrorStatusesTheyCanReturn(string path, string method, string[] statuses)
    {
        var responses = Load().GetProperty("paths").GetProperty(path).GetProperty(method).GetProperty("responses");

        foreach (var status in statuses)
        {
            Assert.True(responses.TryGetProperty(status, out var response), $"{method.ToUpperInvariant()} {path} does not document {status}.");
            Assert.Contains("application/problem+json", ContentTypes(response));
        }
    }

    private static bool TypeIncludesOnlyString(JsonElement property)
    {
        if (!property.TryGetProperty("type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => type.GetString() == "string",
            JsonValueKind.Array => type.EnumerateArray().All(t => t.GetString() is "string" or "null") && type.EnumerateArray().Any(t => t.GetString() == "string"),
            _ => false,
        };
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "RoleRadar.sln")) || Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
