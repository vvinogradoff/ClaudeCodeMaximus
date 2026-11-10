using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Logs one <c>MEM</c> line per interval and, when private memory crosses the spike threshold, writes a heap histogram
/// to the diag folder on a dedicated thread (D1.3, D1.6). Native memory is estimated as private minus GC committed.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProcessMetricsSampler : IProcessMetricsSampler
{
	private readonly DiagnosticsSettingsModel _settings;
	private readonly IRuntimeInspector _inspector;
	private readonly IDiagnosticsArtifactStore _artifacts;
	private readonly TimeProvider _time;
	private readonly ILogger _log;
	private readonly MemorySpikeGate _spikeGate;
	private readonly object _sampleLock = new();
	private readonly CancellationTokenSource _cts = new();

	private Task? _loop;
	private TimeSpan _lastCpu;
	private long _lastCpuTimestamp;

	public ProcessMetricsSampler(
		DiagnosticsSettingsModel settings,
		IRuntimeInspector inspector,
		IDiagnosticsArtifactStore artifacts,
		TimeProvider? time = null,
		ILogger? log = null)
	{
		_settings = settings;
		_inspector = inspector;
		_artifacts = artifacts;
		_time = time ?? TimeProvider.System;
		_log = log ?? Log.ForContext<ProcessMetricsSampler>();
		_spikeGate = new MemorySpikeGate(settings, _time);
	}

	public void Start()
	{
		if (_loop != null)
			return;
		_loop = Task.Factory.StartNew(() => RunAsync(_cts.Token), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
	}

	public ProcessMetricsSnapshotModel GetSnapshot() => Sample(advanceCpuBaseline: false);

	public void Dispose() => _cts.Cancel();

	private async Task RunAsync(CancellationToken token)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _settings.MetricsIntervalSeconds)), _time);
		try
		{
			while (await timer.WaitForNextTickAsync(token))
			{
				try
				{
					Tick();
				}
				catch (Exception ex)
				{
					_log.Warning(ex, "Metrics sampling failed");
				}
			}
		}
		catch (OperationCanceledException)
		{
			// shutting down
		}
	}

	private void Tick()
	{
		var snapshot = Sample(advanceCpuBaseline: true);
		_log.Information(
			Constants.Diagnostics.MetricsLinePrefix + "priv={PrivMb:F0}MB ws={WsMb:F0}MB heap={HeapMb:F0}MB commit={CommitMb:F0}MB frag={FragMb:F0}MB loh={LohMb:F0}MB native~={NativeMb:F0}MB gc={Gen0}/{Gen1}/{Gen2} pause={PausePct:F1}% pauseTotal={PauseMs:F0}ms handles={Handles} threads={Threads} tp={TpThreads} tpq={TpQueue} cpu={Cpu:F0}%",
			snapshot.PrivateMb, snapshot.WorkingSetMb, snapshot.GcHeapMb, snapshot.GcCommittedMb, snapshot.GcFragmentedMb, snapshot.LohMb,
			snapshot.NativeEstimateMb, snapshot.Gen0Collections, snapshot.Gen1Collections, snapshot.Gen2Collections,
			snapshot.GcPausePercent, snapshot.GcTotalPauseMs, snapshot.Handles, snapshot.Threads,
			snapshot.ThreadPoolThreads, snapshot.ThreadPoolPendingWork, snapshot.CpuPercent);

		if (_spikeGate.ShouldCapture(snapshot.PrivateMb))
			StartHeapCapture(snapshot.PrivateMb);
	}

	private void StartHeapCapture(double privateMb)
	{
		_log.Warning("Private memory {PrivMb:F0}MB reached the spike threshold {ThresholdMb}MB; capturing a heap histogram", privateMb, _settings.MemorySpikeThresholdMb);
		var thread = new Thread(() =>
		{
			try
			{
				var path = _artifacts.CreatePath(DiagnosticArtifactKind.Heap);
				var outcome = _inspector.WriteHeapHistogram(path);
				if (outcome.Success)
					_log.Warning("Heap histogram written to {Path}", path);
				else
					_log.Warning("Heap histogram not written: {Reason}", outcome.Message);
				_artifacts.EnforceRetention(DiagnosticArtifactKind.Heap);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "Heap capture on memory spike failed");
			}
		})
		{
			IsBackground = true,
			Name = "HeapSpikeCapture",
		};
		thread.Start();
	}

	/// <summary>An on-demand sample leaves the CPU baseline alone, so the timer's figure still covers its whole interval.</summary>
	private ProcessMetricsSnapshotModel Sample(bool advanceCpuBaseline)
	{
		lock (_sampleLock)
		{
			return SampleLocked(advanceCpuBaseline);
		}
	}

	private ProcessMetricsSnapshotModel SampleLocked(bool advanceCpuBaseline)
	{
		using var process = Process.GetCurrentProcess();
		var gc = GC.GetGCMemoryInfo();
		var mb = Constants.Diagnostics.BytesPerMb;
		var privateMb = process.PrivateMemorySize64 / mb;
		var committedMb = gc.TotalCommittedBytes / mb;

		// Generation index 3 is the large object heap.
		const int lohIndex = 3;
		var lohMb = gc.GenerationInfo.Length > lohIndex ? gc.GenerationInfo[lohIndex].SizeAfterBytes / mb : 0;

		return new ProcessMetricsSnapshotModel(
			_time.GetUtcNow(),
			privateMb,
			process.WorkingSet64 / mb,
			gc.HeapSizeBytes / mb,
			committedMb,
			gc.FragmentedBytes / mb,
			lohMb,
			GC.CollectionCount(0),
			GC.CollectionCount(1),
			GC.CollectionCount(2),
			gc.PauseTimePercentage,
			GC.GetTotalPauseDuration().TotalMilliseconds,
			privateMb - committedMb,
			process.HandleCount,
			process.Threads.Count,
			ThreadPool.ThreadCount,
			ThreadPool.PendingWorkItemCount,
			MeasureCpuPercent(process, advanceCpuBaseline));
	}

	private double MeasureCpuPercent(Process process, bool advanceBaseline)
	{
		var cpu = process.TotalProcessorTime;
		var now = _time.GetTimestamp();
		var percent = 0.0;
		if (_lastCpuTimestamp != 0)
		{
			var wall = _time.GetElapsedTime(_lastCpuTimestamp, now);
			if (wall > TimeSpan.Zero)
				percent = (cpu - _lastCpu).TotalMilliseconds / wall.TotalMilliseconds * 100.0;
		}
		if (advanceBaseline)
		{
			_lastCpu = cpu;
			_lastCpuTimestamp = now;
		}
		return percent;
	}
}
