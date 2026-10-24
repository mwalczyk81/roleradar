using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using RoleRadar.Application;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Import;
using RoleRadar.Application.Tracking.Import.Commands;
using RoleRadar.Application.Tracking.Import.Pipeline;
using RoleRadar.Cli.Commands;
using RoleRadar.Infrastructure;
using RoleRadar.Tests.Integration.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Integration.Import;

[Collection("ContainerIsolatedCollection")]
public sealed class TrackerImportSafetyNetTests : IClassFixture<IsolatedContainerMongoFixture>
{
    private readonly IsolatedContainerMongoFixture _fixture;

    public TrackerImportSafetyNetTests(IsolatedContainerMongoFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UnexpectedException_InCoordinator_PrintsOnlyExceptionTypeAndCorrelationHint_ExitsNonZero_AndDropsScratchDatabase()
    {
        var tempBundleDir = Path.Combine(Path.GetTempPath(), $"rr_safetynet_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempBundleDir);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempBundleDir, "watchlist-companies.csv"),
                "company,tier,source,notes,decision,corrected_notes\r\nAcmeCorp,watch,referral,Some note,,\r\n");

            var dbName = $"rrtest_safety_{Guid.NewGuid():N}";
            var services = BuildServiceProviderWithFailingCoordinator(dbName, "SecretInternalDiagnosticData");
            var command = TrackerImportCommand.Create(services);

            var swOut = new StringWriter();
            var swErr = new StringWriter();
            var origOut = Console.Out;
            var origErr = Console.Error;

            int exitCode;
            try
            {
                Console.SetOut(swOut);
                Console.SetError(swErr);

                // Dry-run mode by default
                var parseResult = command.Parse($"\"{tempBundleDir}\" --default-date 2026-10-24");
                exitCode = await parseResult.InvokeAsync();
            }
            finally
            {
                Console.SetOut(origOut);
                Console.SetError(origErr);
            }

            var stdout = swOut.ToString();
            var stderr = swErr.ToString();

            // 1. Exit code is non-zero
            Assert.Equal(1, exitCode);

            // 2. Stderr contains exception type and correlation reference hint
            Assert.Contains("InvalidOperationException", stderr);
            Assert.Contains("ref:", stderr);

            // 3. Stderr/Stdout NEVER leak secret message or stack trace
            Assert.DoesNotContain("SecretInternalDiagnosticData", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SecretInternalDiagnosticData", stdout, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("at RoleRadar.", stderr, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("at RoleRadar.", stdout, StringComparison.OrdinalIgnoreCase);

            // 4. Assert no scratch database remains orphaned
            var client = new MongoClient(_fixture.HostMongoUri);
            using var dbsCursor = await client.ListDatabaseNamesAsync();
            var dbs = await dbsCursor.ToListAsync();
            Assert.DoesNotContain(dbs, d => d.StartsWith("roleradar_scratch_", StringComparison.OrdinalIgnoreCase));

            await client.DropDatabaseAsync(dbName);
        }
        finally
        {
            Directory.Delete(tempBundleDir, true);
        }
    }

    private ServiceProvider BuildServiceProviderWithFailingCoordinator(string dbName, string sensitiveErrorMessage)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        RoleRadar.Infrastructure.DependencyInjection.AddRoleRadarInfrastructure(services, opts =>
        {
            opts.ConnectionString = _fixture.HostMongoUri;
            opts.DatabaseName = dbName;
        });
        services.AddRoleRadarApplication();

        // Wrap coordinator to simulate a failure inside dry-run execution
        services.AddTransient<ITrackerImportCoordinator>(sp =>
        {
            var scratchManager = sp.GetRequiredService<ITrackerScratchManager>();
            var contextFactory = sp.GetRequiredService<ITrackingContextFactory>();
            var csvParser = sp.GetRequiredService<ICsvParser>();
            var pipelineFactory = sp.GetRequiredService<ITrackerImportPipelineFactory>();

            return new FailingCoordinatorDecorator(
                new TrackerImportCoordinator(scratchManager, contextFactory, csvParser, pipelineFactory),
                scratchManager,
                contextFactory.TargetDatabaseName,
                sensitiveErrorMessage);
        });

        return services.BuildServiceProvider();
    }

    private sealed class FailingCoordinatorDecorator : ITrackerImportCoordinator
    {
        private readonly ITrackerImportCoordinator _inner;
        private readonly ITrackerScratchManager _scratchManager;
        private readonly string _targetDbName;
        private readonly string _errorMessage;

        public FailingCoordinatorDecorator(
            ITrackerImportCoordinator inner,
            ITrackerScratchManager scratchManager,
            string targetDbName,
            string errorMessage)
        {
            _inner = inner;
            _scratchManager = scratchManager;
            _targetDbName = targetDbName;
            _errorMessage = errorMessage;
        }

        public async Task<Result<TrackerImportSummary>> ExecuteAsync(ImportTrackerCommand command, CancellationToken cancellationToken = default)
        {
            // Create a scratch scope to simulate dry run creating a scratch DB, then throw unexpectedly
            await using var scope = await _scratchManager.CreateScopeAsync(_targetDbName, cancellationToken);
            throw new InvalidOperationException(_errorMessage);
        }
    }
}
