using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Posts a heartbeat to the UI thread every 250 ms from its own thread. When the acknowledgement is overdue by
/// <c>HangWarnMs</c> it warns (D1.4); at <c>HangCaptureMs</c> it captures all thread stacks to the diag folder, once per
/// stall and at most once per cooldown. Time and dispatcher are injected so a stall can be simulated with <see cref="Tick"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiHangWatchdog : IUiHangWatchdog
{
	private const long NoHeartbeat = -1;

	private readonly DiagnosticsSettingsModel _settings;
	private readonly IUiDispatcher _dispatcher;
	private readonly IRuntimeInspector _inspector;
	private readonly IDiagnosticsArtifactStore _artifacts;
	private readonly IOperationTimer _operations;
	private readonly IUiThreadTracker _uiThread;
	private readonly TimeProvider _time;
	private readonly ILogger _log;
	private readonly CancellationTokenSource _cts = new();

	private Thread? _thread;

	// Written by the UI thread (ack) and the watchdog thread; read by GetState on any thread.
	private long _pendingSince = NoHeartbeat;
	private long _lastAckTimestamp;
	private UiStallRecordModel? _lastStall;

	// Watchdog-thread only (the heartbeat fields are written there before the post).
	private int _gen2AtPost;
	private TimeSpan _pauseAtPost;
	private bool _stalled;
	private bool _captureHandled;
	private long _stallPostTimestamp;
	private DateTimeOffset _stallStartedAt;
	private int _stallGen2Start;
	private TimeSpan _stallPauseStart;
	private string? _stallOperation;
	private long? _lastCaptureTimestamp;

	public UiHangWatchdog(
		DiagnosticsSettingsModel settings,
		IUiDispatcher dispatcher,
		IRuntimeInspector inspector,
		IDiagnosticsArtifactStore artifacts,
		IOperationTimer operations,
		IUiThreadTracker uiThread,
		TimeProvider? time = null,
		ILogger? log = null)
	{
		_settings = settings;
		_dispatcher = dispatcher;
		_inspector = inspector;
		_artifacts = artifacts;
		_operations = operations;
		_uiThread = uiThread;
		_time = time ?? TimeProvider.System;
		_log = log ?? Log.ForContext<UiHangWatchdog>();
	}

	public void Start()
	{
		if (_thread != null)
			return;
		_thread = new Thread(Loop)
		{
			IsBackground = true,
			Priority = ThreadPriority.AboveNormal,
			Name = Constants.Diagnostics.WatchdogThreadName,
		};
		_thread.Start();
	}

	public void Dispose()
	{
		_cts.Cancel();
		_thread?.Join(Constants.Diagnostics.WatchdogStopJoinMilliseconds);
	}

	public UiWatchdogStateModel GetState()
	{
		var pending = Volatile.Read(ref _pendingSince);
		var stallMs = pending == NoHeartbeat ? 0 : _time.GetElapsedTime(pending).TotalMilliseconds;
		return new UiWatchdogStateModel(
			IsRunning: _thread != null && !_cts.IsCancellationRequested,
			StalledNow: stallMs >= _settings.HangWarnMs,
			CurrentStallMs: stallMs,
			LastStall: Volatile.Read(ref _lastStall),
			CurrentUiOperation: _operations.CurrentOperation,
			UiThreadId: _uiThread.UiThreadId);
	}

	private void Loop()
	{
		var token = _cts.Token;
		while (!token.IsCancellationRequested)
		{
			try
			{
				Tick();
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "UI watchdog tick failed");
			}
			if (token.WaitHandle.WaitOne(Constants.Diagnostics.HeartbeatIntervalMilliseconds))
				return;
		}
	}

	/// <summary>One 250 ms step: post a heartbeat if none is outstanding, otherwise judge how overdue it is.</summary>
	internal void Tick()
	{
		var now = _time.GetTimestamp();
		var pending = Volatile.Read(ref _pendingSince);

		if (pending == NoHeartbeat)
		{
			if (_stalled)
				EndStall();
			PostHeartbeat(now);
			return;
		}

		var stallMs = _time.GetElapsedTime(pending, now).TotalMilliseconds;
		if (stallMs < _settings.HangWarnMs)
			return;

		if (!_stalled)
			BeginStall(pending, stallMs);
		_stallOperation ??= _operations.CurrentOperation;

		if (!_captureHandled && stallMs >= _settings.HangCaptureMs)
			CaptureOrSkip(stallMs);
	}

	private void PostHeartbeat(long now)
	{
		_gen2AtPost = GC.CollectionCount(2);
		_pauseAtPost = GC.GetTotalPauseDuration();
		Volatile.Write(ref _pendingSince, now);
		try
		{
			_dispatcher.Post(Acknowledge);
		}
		catch (Exception ex)
		{
			Volatile.Write(ref _pendingSince, NoHeartbeat);
			_log.Warning(ex, "UI watchdog could not post a heartbeat");
		}
	}

	/// <summary>Runs on the UI thread.</summary>
	private void Acknowledge()
	{
		Volatile.Write(ref _lastAckTimestamp, _time.GetTimestamp());
		Volatile.Write(ref _pendingSince, NoHeartbeat);
	}

	private void BeginStall(long postTimestamp, double stallMs)
	{
		_stalled = true;
		_captureHandled = false;
		_stallPostTimestamp = postTimestamp;
		_stallStartedAt = _time.GetUtcNow() - TimeSpan.FromMilliseconds(stallMs);
		_stallGen2Start = _gen2AtPost;
		_stallPauseStart = _pauseAtPost;
		_stallOperation = _operations.CurrentOperation;
		_log.Warning("UI-HANG no heartbeat for {StallMs:F0}ms; current UI operation: {Op:l}", stallMs, _stallOperation ?? "(none)");
	}

	private void EndStall()
	{
		_stalled = false;
		var durationMs = _time.GetElapsedTime(_stallPostTimestamp, Volatile.Read(ref _lastAckTimestamp)).TotalMilliseconds;
		var gen2 = GC.CollectionCount(2) - _stallGen2Start;
		var pauseMs = (GC.GetTotalPauseDuration() - _stallPauseStart).TotalMilliseconds;
		Volatile.Write(ref _lastStall, new UiStallRecordModel(_stallStartedAt, durationMs, _stallOperation, gen2, pauseMs));
		_log.Warning("UI-HANG recovered after {DurationMs:F0}ms (operation: {Op:l}, gen2 +{Gen2}, gc pause +{PauseMs:F0}ms)",
			durationMs, _stallOperation ?? "(none)", gen2, pauseMs);
	}

	private void CaptureOrSkip(double stallMs)
	{
		_captureHandled = true;
		var cooldown = TimeSpan.FromMinutes(_settings.HangCaptureCooldownMinutes);
		if (_lastCaptureTimestamp is { } last && _time.GetElapsedTime(last) < cooldown)
		{
			_log.Information("UI-HANG stack capture skipped: last capture is within the {Minutes}-minute cooldown", _settings.HangCaptureCooldownMinutes);
			return;
		}

		_lastCaptureTimestamp = _time.GetTimestamp();
		var operation = _stallOperation;
		new Thread(() => Capture(stallMs, operation))
		{
			IsBackground = true,
			Name = Constants.Diagnostics.WatchdogThreadName + "Capture",
		}.Start();
	}

	private void Capture(double stallMs, string? operation)
	{
		try
		{
			var capture = _inspector.CaptureThreadStacks();
			if (!capture.Success)
			{
				_log.Warning("UI-HANG stack capture failed: {Reason}", capture.Text);
				return;
			}

			var path = _artifacts.CreatePath(DiagnosticArtifactKind.Hang);
			var header = new StringBuilder()
				.AppendLine($"UI hang captured {DateTimeOffset.Now:u}")
				.AppendLine($"Stalled for at least {stallMs:F0} ms; current UI operation: {operation ?? "(none)"}")
				.AppendLine($"UI managed thread id: {_uiThread.UiThreadId?.ToString() ?? "(unknown)"}")
				.AppendLine();
			File.WriteAllText(path, header + capture.Text);
			_artifacts.EnforceRetention(DiagnosticArtifactKind.Hang);

			_log.Warning("UI-HANG stacks written to {Path:l}. Top UI frames:{NewLine}{Frames}",
				path, Environment.NewLine,
				string.Join(Environment.NewLine, capture.UiThreadFrames.Take(Constants.Diagnostics.HangStackLogFrames).Select(f => "    " + f)));
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "UI-HANG stack capture failed");
		}
	}
}
