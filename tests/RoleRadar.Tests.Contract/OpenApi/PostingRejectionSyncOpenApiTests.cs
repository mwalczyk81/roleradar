using System.Text.Json;
using Xunit;

namespace RoleRadar.Tests.Contract.OpenApi;

/// <summary>
/// Feature 008 phase 2: the committed OpenAPI snapshot and the generated TypeScript client must both carry
/// the new additive members. The existing snapshot test compares the live document with the snapshot; these
/// tests pin that the members exist at all, so a regeneration that forgot them cannot go unnoticed.
/// </summary>
public sealed class PostingRejectionSyncOpenApiTests
{
    [Fact]
    public async Task Snapshot_DeclaresEventDate_Warnings_AndEventSource_WithExplicitTypes()
    {
        var root = FindRepoRoot();
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "contracts", "openapi", "roleradar-api.json")));
        var schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");

        var request = schemas.GetProperty("TransitionPostingRequestDto").GetProperty("properties");
        Assert.True(request.TryGetProperty("eventDate", out var eventDate), "TransitionPostingRequestDto.eventDate is missing.");
        AssertTypeMentions(eventDate, "string");

        var detail = schemas.GetProperty("PostingDetailDto").GetProperty("properties");
        Assert.True(detail.TryGetProperty("warnings", out var warnings), "PostingDetailDto.warnings is missing.");
        AssertTypeMentions(warnings, "array");

        var warning = schemas.GetProperty("PostingWarningDto").GetProperty("properties");
        Assert.True(warning.TryGetProperty("code", out _));
        Assert.True(warning.TryGetProperty("message", out _));

        var applicationEvent = schemas.GetProperty("ApplicationEventDto").GetProperty("properties");
        Assert.True(applicationEvent.TryGetProperty("eventSource", out var eventSource), "ApplicationEventDto.eventSource is missing.");
        AssertTypeMentions(eventSource, "string");
    }

    [Fact]
    public async Task Snapshot_DeclaresApplicationId_OnPostingDetail_AsANullableUuid()
    {
        var root = FindRepoRoot();
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "contracts", "openapi", "roleradar-api.json")));
        var detail = doc.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("PostingDetailDto").GetProperty("properties");

        Assert.True(detail.TryGetProperty("applicationId", out var applicationId), "PostingDetailDto.applicationId is missing.");
        var text = applicationId.GetRawText();
        Assert.Contains("\"string\"", text, StringComparison.Ordinal);
        Assert.Contains("\"uuid\"", text, StringComparison.Ordinal);
        Assert.Contains("\"null\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GeneratedClientTypes_CarryTheSameMembers()
    {
        var root = FindRepoRoot();
        var raw = await File.ReadAllTextAsync(Path.Combine(root, "contracts", "client", "api-types.ts"));

        // Generated enum members carry JSDoc such as "@enum {string}"; its braces would end the [^}]* matches below.
        var ts = System.Text.RegularExpressions.Regex.Replace(raw, @"/\*\*[\s\S]*?\*/", string.Empty);

        Assert.Contains("PostingWarningDto", ts, StringComparison.Ordinal);
        Assert.Matches(@"TransitionPostingRequestDto:\s*\{[^}]*eventDate\??:", ts);
        Assert.Matches(@"PostingDetailDto:\s*\{[\s\S]*?warnings\??:\s*null \| components\[""schemas""\]\[""PostingWarningDto""\]\[\]", ts);
        Assert.Matches(@"PostingDetailDto:\s*\{[\s\S]*?applicationId\??:\s*null \| string;", ts);
        Assert.Matches(@"ApplicationEventDto:\s*\{[^}]*eventSource\??:", ts);
    }

    private static void AssertTypeMentions(JsonElement schema, string expected)
    {
        var text = schema.GetRawText();
        Assert.Contains($"\"{expected}\"", text, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RoleRadar.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
