using System;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class OperationTimer : IOperationTimer
{
	private readonly IUiThreadTracker _uiThread;
	private readonly TimeProvider _time;
	private readonly ILogger _log;
	private volatile string? _current;

	public OperationTimer(IUiThreadTracker uiThread, TimeProvider? time = null, ILogger? log = null)
	{
		_uiThread = uiThread;
		_time = time ?? TimeProvider.System;
		_log = log ?? Log.ForContext<OperationTimer>();
	}

	public string? CurrentOperation => _current;

	public OperationScope Measure(string operation, int warnMs)
	{
		// Only the UI thread owns the "current UI operation"; scopes on other threads are timed but not published.
		var tracksCurrent = _uiThread.IsUiThread();
		var previous = _current;
		if (tracksCurrent)
			_current = operation;

		return new OperationScope(this, operation, warnMs, _time.GetTimestamp(), previous, tracksCurrent,
			GC.CollectionCount(2), GC.GetTotalPauseDuration());
	}

	public void Complete(in OperationScope scope)
	{
		if (scope.TracksCurrent)
			_current = scope.PreviousOperation;

		var elapsed = _time.GetElapsedTime(scope.StartTimestamp);
		if (elapsed.TotalMilliseconds < scope.WarnMs)
			return;

		_log.Warning(Constants.Diagnostics.SlowUiLogTemplate,
			scope.Operation,
			(long)elapsed.TotalMilliseconds,
			GC.CollectionCount(2) - scope.StartGen2,
			(long)(GC.GetTotalPauseDuration() - scope.StartGcPause).TotalMilliseconds);
	}
}
