using Microsoft.Extensions.Logging;

namespace ApiWeld.Tests.Http;

/// <summary>Collects log messages so a test can assert on them.</summary>
sealed class ListLogger : ILogger
{
	public List<string> Messages { get; } = [];

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
		=> Messages.Add($"{logLevel}: {formatter(state, exception)}");
}
