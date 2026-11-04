namespace RoleRadar.Tests.Architecture;

using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Insights;
using RoleRadar.Application.Reading;
using Xunit;

/// <summary>
/// T066: the Insights stats read is a query. It reads through the named seam only, the Application assembly has no
/// MongoDB types, and the handler is registered as an <see cref="IQueryHandler{TQuery,TResponse}"/> and never wrapped
/// as a command (no transaction, no audit entry: it writes nothing). The existing rule that RoleRadar.Api has zero
/// MongoDB references stays in <c>ODataSeamArchitectureTests</c> and <c>LayerDependencyArchitectureTests</c>.
/// </summary>
public sealed class InsightsStatsArchitectureTests
{
    private static readonly System.Reflection.Assembly ApplicationAssembly = typeof(GetInsightsStatsQueryHandler).Assembly;

    [Fact]
    public void InsightsTypes_InTheApplicationAssembly_DoNotDependOnMongoDb()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .ResideInNamespaceStartingWith("RoleRadar.Application.Insights")
            .ShouldNot()
            .HaveDependencyOnAny("MongoDB.Driver", "MongoDB.Bson")
            .GetResult();

        Assert.True(result.IsSuccessful, "Insights types reference MongoDB: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void TheWholeApplicationAssembly_HasNoMongoDbReferences()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("MongoDB.Driver", "MongoDB.Bson")
            .GetResult();

        Assert.True(result.IsSuccessful, "Application references MongoDB: " + string.Join(", ", result.FailingTypeNames ?? []));
        Assert.DoesNotContain(
            ApplicationAssembly.GetReferencedAssemblies(),
            a => a.Name is not null && a.Name.StartsWith("MongoDB", StringComparison.Ordinal));
    }

    [Fact]
    public void StatsHandler_IsAnIQueryHandler_AndNotAnICommandHandler()
    {
        var interfaces = typeof(GetInsightsStatsQueryHandler).GetInterfaces();

        Assert.Contains(interfaces, i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>));
        Assert.DoesNotContain(interfaces, i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>));
        Assert.True(typeof(IQuery<InsightsStatsDto>).IsAssignableFrom(typeof(GetInsightsStatsQuery)));
        Assert.False(typeof(ICommand<InsightsStatsDto>).IsAssignableFrom(typeof(GetInsightsStatsQuery)));
    }

    [Fact]
    public void StatsHandler_IsRegisteredAsAQueryHandler_AndNotWrappedAsACommand()
    {
        var services = new ServiceCollection();
        services.AddRoleRadarApplication();

        Assert.Contains(services, d => d.ServiceType == typeof(IQueryHandler<GetInsightsStatsQuery, InsightsStatsDto>));
        Assert.DoesNotContain(services, d =>
            d.ServiceType.IsGenericType
            && d.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            && d.ServiceType.GetGenericArguments()[1] == typeof(InsightsStatsDto));
        Assert.DoesNotContain(services, d =>
            d.ServiceType.IsGenericType
            && d.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            && d.ServiceType.GetGenericArguments()[0] == typeof(GetInsightsStatsQuery));
    }

    [Fact]
    public void StatsHandler_ConstructorDependsOnlyOnTheSeamAndTheClock()
    {
        var constructors = typeof(GetInsightsStatsQueryHandler).GetConstructors();

        var constructor = Assert.Single(constructors);
        var parameterTypes = constructor.GetParameters().Select(p => p.ParameterType).ToArray();
        Assert.Contains(typeof(IOwnerReadSeam), parameterTypes);
        Assert.All(parameterTypes, t => Assert.Contains(
            t,
            new[] { typeof(IOwnerReadSeam), typeof(RoleRadar.Domain.Common.IUtcClock) }));
    }

    [Fact]
    public void ApiEndpointsForInsights_UseTheQueryHandler_NotTheSeamOrMongo()
    {
        var apiAssembly = typeof(RoleRadar.Api.Program).Assembly;
        var endpointType = apiAssembly.GetType("RoleRadar.Api.Endpoints.InsightsEndpoints");
        Assert.NotNull(endpointType);

        var result = Types.InAssembly(apiAssembly)
            .That()
            .HaveName("InsightsEndpoints")
            .ShouldNot()
            .HaveDependencyOnAny("MongoDB.Driver", "MongoDB.Bson", "RoleRadar.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, "InsightsEndpoints depends on a forbidden assembly: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
