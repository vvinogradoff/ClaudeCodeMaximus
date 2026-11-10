using System;
using System.Threading.Tasks;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>Logs exceptions nobody handled: AppDomain, unobserved tasks and the UI dispatcher (D1.1).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class GlobalExceptionLogger
{
	private readonly ILogger _log;

	public GlobalExceptionLogger(ILogger? log = null)
	{
		_log = log ?? Log.ForContext<GlobalExceptionLogger>();
	}

	/// <summary>Logs Fatal, then flushes the log: the process is usually about to die.</summary>
	public void OnAppDomainUnhandled(object? sender, UnhandledExceptionEventArgs e)
	{
		_log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (terminating: {IsTerminating}): {Detail}", e.IsTerminating, e.ExceptionObject);
		Log.CloseAndFlush();
	}

	public void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		_log.Error(e.Exception, "Unobserved task exception");
		e.SetObserved();
	}

	/// <summary>Logs only; the caller must not mark the exception handled.</summary>
	public void OnUiThreadException(Exception exception) =>
		_log.Error(exception, "Unhandled UI thread exception");
}
