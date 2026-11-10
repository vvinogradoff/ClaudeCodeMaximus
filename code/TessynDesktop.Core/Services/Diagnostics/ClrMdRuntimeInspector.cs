using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Runtime;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// <see cref="IRuntimeInspector"/> over ClrMD: a copy-on-write process snapshot is attached to and read, so the live
/// threads are never suspended. Windows only. Each capture runs on its own dedicated thread under a timeout.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClrMdRuntimeInspector : IRuntimeInspector
{
	private readonly IUiThreadTracker _uiThread;
	private readonly DiagnosticsSettingsModel _settings;
	private readonly ILogger _log;
	private int _busy;

	public ClrMdRuntimeInspector(IUiThreadTracker uiThread, DiagnosticsSettingsModel settings, ILogger? log = null)
	{
		_uiThread = uiThread;
		_settings = settings;
		_log = log ?? Log.ForContext<ClrMdRuntimeInspector>();
	}

	public ThreadStackCaptureModel CaptureThreadStacks()
	{
		var uiFrames = new List<string>();
		var text = new StringBuilder();
		var error = Run("thread stack capture", Constants.Diagnostics.StackCaptureTimeoutSeconds,
			() => WriteStacks(text, uiFrames));
		return error == null
			? new ThreadStackCaptureModel(true, text.ToString(), uiFrames)
			: new ThreadStackCaptureModel(false, error, uiFrames);
	}

	public InspectionOutcomeModel WriteHeapHistogram(string path)
	{
		var error = Run("heap histogram", Constants.Diagnostics.HeapCaptureTimeoutSeconds, () => WriteHistogram(path));
		return new InspectionOutcomeModel(error == null, error ?? path);
	}

	public InspectionOutcomeModel WriteDump(string path)
	{
		if (!_settings.DumpsEnabled)
			return new InspectionOutcomeModel(false, "Dumps are disabled: set Diagnostics.DumpsEnabled to true in appsettings.json and restart.");

		var error = Run("process dump", Constants.Diagnostics.DumpCaptureTimeoutSeconds,
			() => new DiagnosticsClient(Environment.ProcessId).WriteDump(DumpType.WithHeap, path));
		return new InspectionOutcomeModel(error == null, error ?? path);
	}

	/// <summary>Runs <paramref name="work"/> on a dedicated thread; returns null on success, otherwise why it did not complete.</summary>
	private string? Run(string what, int timeoutSeconds, Action work)
	{
		if (!OperatingSystem.IsWindows())
			return $"{what} skipped: only supported on Windows";
		if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
			return $"{what} skipped: another capture is already running";

		Exception? failure = null;
		var task = Task.Factory.StartNew(() =>
		{
			try
			{
				work();
			}
			catch (Exception ex)
			{
				failure = ex;
			}
			finally
			{
				Volatile.Write(ref _busy, 0);
			}
		}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

		if (!task.Wait(TimeSpan.FromSeconds(timeoutSeconds)))
		{
			_log.Warning("Diagnostics {What} still running after {Seconds}s; giving up waiting", what, timeoutSeconds);
			return $"{what} timed out after {timeoutSeconds}s (it keeps running in the background; its output is discarded)";
		}

		if (failure == null)
			return null;

		_log.Warning(failure, "Diagnostics {What} failed", what);
		return $"{what} failed: {failure.GetType().Name}: {failure.Message}";
	}

	private void WriteStacks(StringBuilder text, List<string> uiFrames)
	{
		using var target = DataTarget.CreateSnapshotAndAttach(Environment.ProcessId);
		using var runtime = target.ClrVersions.First().CreateRuntime();
		var uiId = _uiThread.UiThreadId;

		foreach (var thread in runtime.Threads.Where(t => t.IsAlive))
		{
			var isUi = uiId != null && thread.ManagedThreadId == uiId;
			text.AppendLine($"--- managed thread {thread.ManagedThreadId} (os {thread.OSThreadId:x}){(isUi ? " [UI]" : string.Empty)}");

			var limit = isUi ? int.MaxValue : Constants.Diagnostics.HangStackMaxOtherFrames;
			var written = 0;
			foreach (var frame in thread.EnumerateStackTrace())
			{
				if (written == limit)
				{
					text.AppendLine("    ...");
					break;
				}
				var line = frame.ToString() ?? string.Empty;
				text.AppendLine("    " + line);
				if (isUi)
					uiFrames.Add(line);
				written++;
			}
			text.AppendLine();
		}
	}

	private static void WriteHistogram(string path)
	{
		using var target = DataTarget.CreateSnapshotAndAttach(Environment.ProcessId);
		using var runtime = target.ClrVersions.First().CreateRuntime();
		var heap = runtime.Heap;
		if (!heap.CanWalkHeap)
			throw new InvalidOperationException("The heap cannot be walked in this snapshot");

		var byType = new Dictionary<ClrType, HeapTypeTotalsModel>();
		var bySegment = new Dictionary<GCSegmentKind, HeapTypeTotalsModel>();
		var unknown = new HeapTypeTotalsModel();

		foreach (var segment in heap.Segments)
		{
			if (!bySegment.TryGetValue(segment.Kind, out var segmentTotals))
				bySegment[segment.Kind] = segmentTotals = new HeapTypeTotalsModel();

			foreach (var obj in segment.EnumerateObjects())
			{
				// A snapshot can catch an object mid-allocation: its type is unreadable and so is its size.
				var type = obj.Type;
				if (type == null)
				{
					unknown.Count++;
					continue;
				}

				var size = obj.Size;
				segmentTotals.Count++;
				segmentTotals.Bytes += size;
				if (!byType.TryGetValue(type, out var totals))
					byType[type] = totals = new HeapTypeTotalsModel();
				totals.Count++;
				totals.Bytes += size;
			}
		}

		var named = byType.Select(p => (Name: p.Key.Name ?? "<unnamed>", p.Value)).ToList();
		var top = Constants.Diagnostics.HeapHistogramTopCount;
		var mb = Constants.Diagnostics.BytesPerMb;
		var sb = new StringBuilder();
		sb.AppendLine($"Heap histogram taken {DateTimeOffset.Now:u}");
		sb.AppendLine($"Objects: {bySegment.Values.Sum(t => t.Count):N0}, bytes: {bySegment.Values.Sum(t => (double)t.Bytes) / mb:N1} MB, types: {named.Count:N0}, unknown-type objects: {unknown.Count:N0}");
		sb.AppendLine();
		sb.AppendLine("== Totals per GC segment kind ==");
		foreach (var (kind, totals) in bySegment.OrderByDescending(p => p.Value.Bytes).Select(p => (p.Key, p.Value)))
			sb.AppendLine($"{kind,-14} {totals.Bytes / mb,12:N1} MB {totals.Count,14:N0} objects");
		sb.AppendLine();
		sb.AppendLine($"== Top {top} types by total bytes ==");
		foreach (var (name, totals) in named.OrderByDescending(t => t.Value.Bytes).Take(top))
			sb.AppendLine($"{totals.Bytes / mb,12:N1} MB {totals.Count,14:N0}  {name}");
		sb.AppendLine();
		sb.AppendLine($"== Top {top} types by count ==");
		foreach (var (name, totals) in named.OrderByDescending(t => t.Value.Count).Take(top))
			sb.AppendLine($"{totals.Count,14:N0} {totals.Bytes / mb,12:N1} MB  {name}");

		File.WriteAllText(path, sb.ToString());
	}
}
