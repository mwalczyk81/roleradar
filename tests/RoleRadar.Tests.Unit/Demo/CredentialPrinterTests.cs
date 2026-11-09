using System.CommandLine;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RoleRadar.Cli.Demo;
using RoleRadar.Identity.Data;
using RoleRadar.Infrastructure.Mongo;
using Xunit;

namespace RoleRadar.Tests.Unit.Demo;

/// <summary>
/// The credentials command prints username, password and TOTP secret once, to an interactive terminal only, and never
/// through a logger (FR-007a). The totp and credentials commands run the guard first (FR-004).
/// </summary>
public sealed class CredentialPrinterTests
{
    private static readonly DemoCredentials Sample = new(DemoAccount.Username, "scratch-password-value", "SCRATCHKEYBASE32VALUE", "otpauth://totp/RoleRadar:scratch?secret=SCRATCHKEYBASE32VALUE&issuer=RoleRadar");

    [Fact]
    public void Print_WhenNotInteractive_RefusesAndWritesNothing()
    {
        var writer = new StringWriter();

        var printed = CredentialPrinter.TryPrint(Sample, writer, isInteractive: () => false);

        Assert.False(printed);
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void Print_WhenInteractive_WritesEachSecretExactlyOnce()
    {
        var writer = new StringWriter();

        var printed = CredentialPrinter.TryPrint(Sample, writer, isInteractive: () => true);

        Assert.True(printed);
        var text = writer.ToString();
        Assert.Contains(Sample.Username, text, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, System.Text.RegularExpressions.Regex.Escape(Sample.Password)));
        Assert.Contains(Sample.AuthenticatorKey, text, StringComparison.Ordinal);
        Assert.Contains("otpauth://", text, StringComparison.Ordinal);
        Assert.Contains("only once", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CredentialsCommand_WhenNotInteractive_ExitsWithTwoAndPrintsNoSecret()
    {
        using var harness = await Harness.CreateAsync(interactive: false, demoMode: "1", database: DemoModeGuard.DemoDatabaseName);

        var exit = await harness.RunAsync("credentials");

        Assert.Equal(2, exit);
        Assert.DoesNotContain("password", harness.Output.ToString() + harness.Error, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("otpauth", harness.Output.ToString() + harness.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CredentialsCommand_WhenInteractive_PrintsOnceResetsTheAccountAndLogsNothing()
    {
        using var harness = await Harness.CreateAsync(interactive: true, demoMode: "1", database: DemoModeGuard.DemoDatabaseName);
        await new DemoAccountManager(harness.Identity).EnsureAccountAsync(TestContext.Current.CancellationToken);
        var hashBefore = (await harness.Identity.Users.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).PasswordHash;

        var exit = await harness.RunAsync("credentials");

        Assert.Equal(0, exit);
        var output = harness.Output.ToString();
        Assert.Contains(DemoAccount.Username, output, StringComparison.Ordinal);
        Assert.Contains("otpauth://", output, StringComparison.Ordinal);
        var hashAfter = (await harness.Identity.Users.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).PasswordHash;
        Assert.NotEqual(hashBefore, hashAfter);
        Assert.Equal(string.Empty, harness.Error.ToString());

        // Demo code logs nothing, and no log entry from anywhere (EF Core logs its own migration work) holds a secret.
        Assert.DoesNotContain(harness.Logger.Entries, e => e.Category.StartsWith("RoleRadar", StringComparison.Ordinal));
        var password = System.Text.RegularExpressions.Regex.Match(output, @"Password:\s+(\S+)").Groups[1].Value;
        var key = System.Text.RegularExpressions.Regex.Match(output, @"TOTP secret:\s+(\S+)").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(password));
        Assert.False(string.IsNullOrEmpty(key));
        Assert.DoesNotContain(harness.Logger.Entries, e => e.Message.Contains(password, StringComparison.Ordinal) || e.Message.Contains(key, StringComparison.Ordinal));
        Assert.DoesNotContain(harness.Logger.Entries, e => e.Message.Contains("otpauth", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("credentials")]
    [InlineData("totp")]
    [InlineData("seed")]
    public async Task EveryDemoCommand_RefusesBeforeAnyWork_WhenDemoModeIsOff(string command)
    {
        using var harness = await Harness.CreateAsync(interactive: true, demoMode: null, database: DemoModeGuard.DemoDatabaseName);

        var exit = await harness.RunAsync(command);

        Assert.Equal(2, exit);
        Assert.Empty(await harness.Identity.Users.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("credentials")]
    [InlineData("totp")]
    [InlineData("seed")]
    public async Task EveryDemoCommand_RefusesTheRealDatabaseName(string command)
    {
        using var harness = await Harness.CreateAsync(interactive: true, demoMode: "1", database: "roleradar");

        var exit = await harness.RunAsync(command);

        Assert.Equal(2, exit);
        Assert.Empty(await harness.Identity.Users.ToListAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("mongodb://", harness.Error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TotpCommand_PrintsTheCurrentCode_ButNeverTheSecret()
    {
        using var harness = await Harness.CreateAsync(interactive: true, demoMode: "1", database: DemoModeGuard.DemoDatabaseName);
        var manager = new DemoAccountManager(harness.Identity);
        await manager.EnsureAccountAsync(TestContext.Current.CancellationToken);
        var credentials = await manager.ResetCredentialsAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(credentials);

        var exit = await harness.RunAsync("totp");

        Assert.Equal(0, exit);
        Assert.Matches("[0-9]{6}", harness.Output.ToString());
        Assert.DoesNotContain(credentials.AuthenticatorKey, harness.Output.ToString(), StringComparison.Ordinal);
    }

    private sealed record LogEntry(string Category, string Message);

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CategoryLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CategoryLogger(RecordingLoggerProvider owner, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                owner.Entries.Add(new LogEntry(category, formatter(state, exception)));
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly DemoCommandHost _host;

        private Harness(SqliteConnection connection, ServiceProvider provider, IdentityAppDbContext identity, RecordingLoggerProvider logger, DemoCommandHost host, StringWriter output, StringWriter error)
        {
            _connection = connection;
            _provider = provider;
            Identity = identity;
            Logger = logger;
            _host = host;
            Output = output;
            Error = error;
        }

        public IdentityAppDbContext Identity { get; }

        public RecordingLoggerProvider Logger { get; }

        public StringWriter Output { get; }

        public StringWriter Error { get; }

        public static async Task<Harness> CreateAsync(bool interactive, string? demoMode, string database)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var logger = new RecordingLoggerProvider();
            var services = new ServiceCollection();
            services.AddLogging(b => b.ClearProviders().AddProvider(logger));
            services.AddDbContext<IdentityAppDbContext>(o => o.UseSqlite(connection));
            services.AddSingleton<IOptions<MongoOptions>>(Options.Create(new MongoOptions
            {
                DatabaseName = database,
                ConnectionString = "mongodb://" + DemoModeGuard.DemoMongoHost + ":27017/?directConnection=true"
            }));
            var provider = services.BuildServiceProvider();
            var identity = provider.GetRequiredService<IdentityAppDbContext>();
            await identity.Database.MigrateAsync(TestContext.Current.CancellationToken);

            var output = new StringWriter();
            var error = new StringWriter();
            var host = new DemoCommandHost(
                name => name == DemoModeGuard.DemoModeVariable ? demoMode : null,
                () => interactive,
                output,
                error);
            return new Harness(connection, provider, identity, logger, host, output, error);
        }

        public async Task<int> RunAsync(string subcommand)
        {
            var command = DemoCommand.Create(_provider, _host);
            return await command.Parse(subcommand).InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        }

        public void Dispose()
        {
            Identity.Dispose();
            _provider.Dispose();
            _connection.Dispose();
        }
    }
}
