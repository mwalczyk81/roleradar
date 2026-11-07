using System.Reflection;
using RoleRadar.Api.OpenApi;
using RoleRadar.Domain.Tracking;
using Xunit;

namespace RoleRadar.Tests.Contract;

/// <summary>
/// The enum contract is generated from <see cref="EnumContractRegistry"/>. These guards make the registry complete:
/// a Domain enum property that is neither registered nor allowlisted with a reason fails here.
/// </summary>
public sealed class EnumContractRegistryTests
{
    private static Type? EnumOf(Type propertyType)
    {
        var type = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (type.IsEnum)
        {
            return type;
        }

        if (type.IsArray && type.GetElementType() is { } element)
        {
            return EnumOf(element);
        }

        if (type.IsGenericType && type.GetGenericArguments() is [var only])
        {
            return EnumOf(only);
        }

        return null;
    }

    private static IReadOnlyList<(Type Owner, string Name, Type EnumType)> DomainEnumProperties() =>
        [.. typeof(Company).Assembly.GetExportedTypes()
            .Where(t => !t.IsEnum)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Select(p => (Owner: t, p.Name, EnumType: EnumOf(p.PropertyType)))
                .Where(x => x.EnumType is not null)
                .Select(x => (x.Owner, x.Name, EnumType: x.EnumType!)))
            .OrderBy(x => x.Owner.FullName, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.Ordinal)];

    [Fact]
    public void EveryDomainEnumProperty_IsRegisteredOrAllowlisted()
    {
        var known = EnumContractRegistry.Entries.Select(e => (e.DomainOwner, e.DomainProperty))
            .Concat(EnumContractRegistry.Allowlist.Select(a => (a.DomainOwner, a.DomainProperty)))
            .ToHashSet();

        var missing = DomainEnumProperties()
            .Where(p => !known.Contains((p.Owner, p.Name)))
            .Select(p => $"{p.Owner.FullName}.{p.Name} ({p.EnumType.Name})")
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Domain enum properties missing from EnumContractRegistry (register them or allowlist with a reason): " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryRegistryEntry_PointsAtARealDomainPropertyOfTheSameEnum()
    {
        var domain = DomainEnumProperties().ToDictionary(p => (p.Owner, p.Name), p => p.EnumType);
        var problems = new List<string>();

        foreach (var entry in EnumContractRegistry.Entries)
        {
            if (!domain.TryGetValue((entry.DomainOwner, entry.DomainProperty), out var actual))
            {
                problems.Add($"{entry.DomainOwner.Name}.{entry.DomainProperty} is not a Domain enum property");
            }
            else if (actual != entry.EnumType)
            {
                problems.Add($"{entry.DomainOwner.Name}.{entry.DomainProperty} is {actual.Name}, registry says {entry.EnumType.Name}");
            }

            foreach (var carrier in entry.CarriedBy)
            {
                if (carrier.Dto.GetProperty(carrier.ClrProperty) is null)
                {
                    problems.Add($"{carrier.Dto.Name}.{carrier.ClrProperty} does not exist");
                }
            }
        }

        foreach (var allowed in EnumContractRegistry.Allowlist)
        {
            if (!domain.ContainsKey((allowed.DomainOwner, allowed.DomainProperty)))
            {
                problems.Add($"allowlisted {allowed.DomainOwner.Name}.{allowed.DomainProperty} is not a Domain enum property");
            }
        }

        Assert.True(problems.Count == 0, string.Join("; ", problems));
    }

    [Fact]
    public void EveryAllowlistEntry_HasAReason_AndNothingIsBothRegisteredAndAllowlisted()
    {
        var blank = EnumContractRegistry.Allowlist
            .Where(a => string.IsNullOrWhiteSpace(a.Reason))
            .Select(a => $"{a.DomainOwner.Name}.{a.DomainProperty}")
            .ToArray();
        Assert.True(blank.Length == 0, "Allowlist entries without a reason: " + string.Join(", ", blank));

        var registered = EnumContractRegistry.Entries.Select(e => (e.DomainOwner, e.DomainProperty)).ToHashSet();
        var both = EnumContractRegistry.Allowlist
            .Where(a => registered.Contains((a.DomainOwner, a.DomainProperty)))
            .Select(a => $"{a.DomainOwner.Name}.{a.DomainProperty}")
            .ToArray();
        Assert.True(both.Length == 0, "Both registered and allowlisted: " + string.Join(", ", both));
    }

    [Fact]
    public void TheFiveMandatoryEnums_AreRegisteredAndCarriedByTheApi()
    {
        Type[] mandatory =
        [
            typeof(PostingDisposition),
            typeof(ApplicationOutcome),
            typeof(CompanyStatus),
            typeof(OwnershipType),
            typeof(ApplicationEventType)
        ];

        foreach (var enumType in mandatory)
        {
            var entries = EnumContractRegistry.Entries.Where(e => e.EnumType == enumType).ToArray();
            Assert.True(entries.Length > 0, $"{enumType.Name} is not registered");
            Assert.True(entries.Any(e => e.CarriedBy.Count > 0), $"{enumType.Name} is registered but no DTO property carries it");
        }
    }
}
