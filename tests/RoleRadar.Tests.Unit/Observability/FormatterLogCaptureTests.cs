// Tests log fixed strings to exercise the sink; LoggerMessage delegates would add nothing here.
#pragma warning disable CA1848, CA1873

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

public sealed class FormatterLogCaptureTests
{
    /// <summary>Writes "{level}|{message}|{scopes}" so the sink's behavior is visible without the production formatter.</summary>
    private sealed class PipeFormatter : ConsoleFormatter
    {
        public PipeFormatter() : base("pipe") { }

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
        {
            var scopes = new List<string>();
            scopeProvider?.ForEachScope((scope, list) => list.Add(scope?.ToString() ?? string.Empty), scopes);
            textWriter.Write($"{logEntry.LogLevel}|{logEntry.Formatter(logEntry.State, logEntry.Exception)}|{string.Join(",", scopes)}\n");
        }
    }

    [Fact]
    public void WritesOneLinePerLogCall()
    {
        using var capture = new FormatterLogCapture(new PipeFormatter());
        var logger = capture.CreateLogger("Test");

        logger.LogInformation("first");
        logger.LogWarning("second");

        Assert.Equal(["Information|first|", "Warning|second|"], capture.Lines);
    }

    [Fact]
    public void PassesScopesToTheFormatter()
    {
        using var capture = new FormatterLogCapture(new PipeFormatter());
        var logger = capture.CreateLogger("Test");

        using (logger.BeginScope("outer"))
        using (logger.BeginScope("inner"))
        {
            logger.LogInformation("inside");
        }

        logger.LogInformation("outside");

        Assert.Equal(["Information|inside|outer,inner", "Information|outside|"], capture.Lines);
    }

    [Fact]
    public void InstancesDoNotShareOutput()
    {
        using var first = new FormatterLogCapture(new PipeFormatter());
        using var second = new FormatterLogCapture(new PipeFormatter());

        first.CreateLogger("A").LogInformation("only in first");

        Assert.Single(first.Lines);
        Assert.Empty(second.Lines);
    }

    [Fact]
    public void ClearDropsCapturedLines()
    {
        using var capture = new FormatterLogCapture(new PipeFormatter());
        capture.CreateLogger("Test").LogInformation("gone");

        capture.Clear();

        Assert.Empty(capture.Lines);
    }

    [Fact]
    public void WorksAsAProviderInALoggerFactory()
    {
        using var capture = new FormatterLogCapture(new PipeFormatter());
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(capture));

        var logger = factory.CreateLogger("Test");
        using (logger.BeginScope("factory-scope"))
        {
            logger.LogInformation("via factory");
        }

        Assert.Equal(["Information|via factory|factory-scope"], capture.Lines);
        Assert.NotNull(NullLogger.Instance);
    }
}
