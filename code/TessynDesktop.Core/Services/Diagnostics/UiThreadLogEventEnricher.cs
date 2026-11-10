using System;
using Serilog.Core;
using Serilog.Events;

namespace TessynDesktop.Services;

/// <summary>Adds the managed thread id and a <c>|UI</c> marker (on the UI thread) to every log event.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiThreadLogEventEnricher : ILogEventEnricher
{
	private readonly IUiThreadTracker _uiThread;

	public UiThreadLogEventEnricher(IUiThreadTracker uiThread)
	{
		_uiThread = uiThread;
	}

	public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
	{
		logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(Constants.Diagnostics.ThreadIdPropertyName, Environment.CurrentManagedThreadId));
		logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
			Constants.Diagnostics.UiMarkerPropertyName,
			_uiThread.IsUiThread() ? Constants.Diagnostics.UiMarkerText : string.Empty));
	}
}
