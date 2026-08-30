using Microsoft.Extensions.Logging;

namespace ExtraGasMVC.Tests;

/// <summary>
/// Spy de <see cref="ILogger{T}"/> que captura todas las entradas en memoria.
/// Usado por los tests de logging para verificar nivel, mensaje y excepcion
/// sin necesidad de Moq (que no esta en el proyecto). <see cref="NullLogger{T}"/>
/// sigue disponible para tests que no les importa el logging.
/// </summary>
public sealed class TestLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
    }
}

public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
