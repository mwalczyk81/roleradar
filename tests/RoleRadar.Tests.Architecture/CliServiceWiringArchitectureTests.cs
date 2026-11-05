namespace RoleRadar.Tests.Architecture;

using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Cli;
using RoleRadar.Infrastructure.Mongo.Reconcile;
using Xunit;

public sealed class CliServiceWiringArchitectureTests
{
    [Fact]
    public void CliContainer_ResolvesReadModelReconciliationService_And_Interface()
    {
        // Build the service provider the exact way RoleRadar.Cli/Program.cs does
        using var services = Program.BuildServiceProvider(configure: sc =>
        {
            // Provide a dummy connection string if not present in env so infrastructure can configure mongo
            Environment.SetEnvironmentVariable("ROLERADAR_MONGO_CONNECTION_STRING", "mongodb://localhost:27017");
        });

        using var scope = services.CreateScope();

        // Must resolve the concrete service and its interface without throwing
        var concreteService = scope.ServiceProvider.GetService<ReadModelReconciliationService>();
        var interfaceService = scope.ServiceProvider.GetService<IReadModelReconciliationService>();

        Assert.NotNull(concreteService);
        Assert.NotNull(interfaceService);
        Assert.Same(concreteService, interfaceService);
    }

    [Fact]
    public void CliContainer_RootProvider_ResolvesReadModelReconciliationService_WithoutManualScope()
    {
        // ReconcileReadModelsCommand receives the root IServiceProvider at startup.
        // It must be resolvable from root services or via scoped resolution.
        using var services = Program.BuildServiceProvider(configure: sc =>
        {
            Environment.SetEnvironmentVariable("ROLERADAR_MONGO_CONNECTION_STRING", "mongodb://localhost:27017");
        });

        // Command invokes services.GetRequiredService<ReadModelReconciliationService>()
        var concreteService = services.GetService<ReadModelReconciliationService>();
        var interfaceService = services.GetService<IReadModelReconciliationService>();

        Assert.NotNull(concreteService);
        Assert.NotNull(interfaceService);
    }
}
