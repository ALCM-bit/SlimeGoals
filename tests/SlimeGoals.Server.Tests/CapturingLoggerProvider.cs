using Microsoft.Extensions.Logging;

namespace SlimeGoals.Server.Tests;

/// <summary>Guarda cada linha de log (mensagem e exceção) para os testes de privacidade.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get { lock (_lines) { return [.. _lines]; } }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var line = $"{category} {formatter(state, exception)} {exception}";
            lock (owner._lines)
            {
                owner._lines.Add(line);
            }
        }
    }
}
