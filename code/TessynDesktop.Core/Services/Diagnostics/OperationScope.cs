using System;

namespace TessynDesktop.Services;

/// <summary>Handle returned by <see cref="IOperationTimer.Measure"/>; dispose it when the operation ends.</summary>
/// <remarks>Created by Claude</remarks>
public readonly struct OperationScope : IDisposable
{
	private readonly IOperationTimer? _timer;

	public string Operation { get; }
	public int WarnMs { get; }
	public long StartTimestamp { get; }
	public string? PreviousOperation { get; }
	public bool TracksCurrent { get; }
	public int StartGen2 { get; }
	public TimeSpan StartGcPause { get; }

	public OperationScope(IOperationTimer timer, string operation, int warnMs, long startTimestamp,
		string? previousOperation, bool tracksCurrent, int startGen2, TimeSpan startGcPause)
	{
		_timer = timer;
		Operation = operation;
		WarnMs = warnMs;
		StartTimestamp = startTimestamp;
		PreviousOperation = previousOperation;
		TracksCurrent = tracksCurrent;
		StartGen2 = startGen2;
		StartGcPause = startGcPause;
	}

	public void Dispose() => _timer?.Complete(this);
}
