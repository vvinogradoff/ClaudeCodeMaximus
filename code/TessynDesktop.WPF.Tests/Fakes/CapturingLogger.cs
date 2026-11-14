using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Collects the formatted log messages.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CapturingLogger<T> : ILogger<T>
{
	public List<string> Messages { get; } = [];

	public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

	public bool IsEnabled(LogLevel logLevel) => true;

	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
	{
		lock (Messages)
			Messages.Add(formatter(state, exception));
	}
}
