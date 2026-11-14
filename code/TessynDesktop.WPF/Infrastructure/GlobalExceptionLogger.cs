using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Infrastructure;

/// <summary>Logs every unhandled exception of the UI thread, other threads and unobserved tasks.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class GlobalExceptionLogger
{
	private readonly ILogger<GlobalExceptionLogger> _logger;

	public GlobalExceptionLogger(ILogger<GlobalExceptionLogger> logger)
	{
		_logger = logger;
	}

	public void Attach(System.Windows.Application app)
	{
		app.DispatcherUnhandledException += OnDispatcherUnhandled;
		AppDomain.CurrentDomain.UnhandledException += (_, e) => _logger.LogCritical(e.ExceptionObject as Exception, "Unhandled exception (terminating: {Terminating})", e.IsTerminating);
		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			_logger.LogError(e.Exception, "Unobserved task exception");
			e.SetObserved();
		};
	}

	private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		_logger.LogError(e.Exception, "Unhandled UI-thread exception");
		e.Handled = true;
	}
}
