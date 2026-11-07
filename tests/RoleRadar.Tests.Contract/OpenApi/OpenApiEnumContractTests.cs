using System.Reflection;
using System.Text.Json;
using RoleRadar.Api.Models;
using RoleRadar.Api.OpenApi;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Contract.OpenApi;

/// <summary>
/// The committed OpenAPI document must carry <c>enum</c> for every registered property, with the Domain enum's
/// serialized values in declaration order. ApiOpenApiContractSnapshotTests separately keeps the file equal to the live API.
/// </summary>
public sealed class OpenApiEnumContractTests
{
    private static JsonElement Schemas()
    {
        var path = Path.Combine(FindRepoRoot(), "contracts", "openapi", "roleradar-api.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("components").GetProperty("schemas").Clone();
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

    private static string[] DomainSerializedValues(Type enumType)
    {
        var method = typeof(Company).Assembly.GetExportedTypes()
            .Where(t => t.IsAbstract && t.IsSealed)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Single(m => m.Name == "ToSnakeCase" && m.GetParameters() is [var p] && p.ParameterType == enumType);

        return [.. Enum.GetValues(enumType).Cast<object>().Select(v => (string)method.Invoke(null, [v])!)];
    }

    private static JsonElement PropertySchema(JsonElement schemas, EnumDtoProperty carrier)
    {
        var json = JsonNamingPolicy.CamelCase.ConvertName(carrier.ClrProperty);
        return schemas.GetProperty(carrier.Dto.Name).GetProperty("properties").GetProperty(json);
    }

    private static string[] EnumValues(JsonElement property)
    {
        var holder = property.TryGetProperty("items", out var items) ? items : property;
        Assert.True(holder.TryGetProperty("enum", out var values), "property has no enum");
        return [.. values.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)];
    }

    [Fact]
    public void EveryRegisteredProperty_CarriesTheDomainEnumValuesInDeclarationOrder()
    {
        var schemas = Schemas();
        var problems = new List<string>();

        foreach (var entry in EnumContractRegistry.Entries)
        {
            var expected = DomainSerializedValues(entry.EnumType);
            foreach (var carrier in entry.CarriedBy)
            {
                try
                {
                    var actual = EnumValues(PropertySchema(schemas, carrier));
                    if (!expected.SequenceEqual(actual))
                    {
                        problems.Add($"{carrier.Dto.Name}.{carrier.ClrProperty}: expected [{string.Join(",", expected)}] got [{string.Join(",", actual)}]");
                    }
                }
                catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or Xunit.Sdk.XunitException)
                {
                    problems.Add($"{carrier.Dto.Name}.{carrier.ClrProperty}: {ex.Message}");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheFiveMandatoryProperties_HaveEnumInTheCommittedDocument()
    {
        var schemas = Schemas();
        (Type Dto, string Property, Type EnumType)[] mandatory =
        [
            (typeof(PostingDetailDto), nameof(PostingDetailDto.Disposition), typeof(PostingDisposition)),
            (typeof(ApplicationDetailDto), nameof(ApplicationDetailDto.Status), typeof(ApplicationOutcome)),
            (typeof(CompanyDetailDto), nameof(CompanyDetailDto.Status), typeof(CompanyStatus)),
            (typeof(CompanyDetailDto), nameof(CompanyDetailDto.OwnershipType), typeof(OwnershipType)),
            (typeof(ApplicationEventDto), nameof(ApplicationEventDto.EventType), typeof(ApplicationEventType))
        ];

        foreach (var (dto, property, enumType) in mandatory)
        {
            var actual = EnumValues(PropertySchema(schemas, new EnumDtoProperty(dto, property)));
            Assert.Equal(DomainSerializedValues(enumType), actual);
        }
    }
}
