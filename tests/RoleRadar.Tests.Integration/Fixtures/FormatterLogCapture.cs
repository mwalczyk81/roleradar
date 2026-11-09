using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace RoleRadar.Tests;

/// <summary>
/// Test-only log sink (feature 013, ADR 0041). Runs any <see cref="ConsoleFormatter"/>, normally the production
/// <c>RoleRadarConsoleFormatter</c>, into a per-instance <see cref="StringWriter"/>, so tests assert on exactly the
/// bytes a host would write. Not part of RoleRadar.Observability's public API. Linked into the Contract and Unit
/// projects, so there is one copy of this file. Each test owns its own instance, which keeps parallel tests apart.
/// </summary>
internal sealed class FormatterLogCapture : ILoggerProvider, ISupportExternalScope
{
    private readonly ConsoleFormatter _formatter;
    private readonly StringBuilder _buffer = new();
    private readonly object _gate = new();
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public FormatterLogCapture(ConsoleFormatter formatter)
    {
        _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    }

    /// <summary>Everything written so far.</summary>
    public string Text
    {
        get
        {
            lock (_gate)
            {
                return _buffer.ToString();
            }
        }
    }

    /// <summary>The written output split on line feeds, without the empty tail after the last line.</summary>
    public IReadOnlyList<string> Lines =>
        Text.Split('\n').Where(line => line.Length > 0).ToArray();

    public void Clear()
    {
        lock (_gate)
        {
            _buffer.Clear();
        }
    }

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) =>
        _scopes = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));

    public void Dispose()
    {
    }

    private void Write<TState>(LogLevel level, string category, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> format)
    {
        var entry = new LogEntry<TState>(level, category, eventId, state, exception, format);
        using var writer = new StringWriter();
        _formatter.Write(in entry, _scopes, writer);

        lock (_gate)
        {
            _buffer.Append(writer.ToString());
        }
    }

    private sealed class CaptureLogger(FormatterLogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            owner.Write(logLevel, category, eventId, state, exception, formatter);
        }
    }
}
