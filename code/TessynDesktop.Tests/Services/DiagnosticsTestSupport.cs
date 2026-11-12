using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>Clock with a millisecond-resolution timestamp the test advances by hand.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class StepTimeProvider : TimeProvider
{
	private long _ms = 1000;

	public override long TimestampFrequency => 1000;

	public override long GetTimestamp() => Interlocked.Read(ref _ms);

	public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero).AddMilliseconds(Interlocked.Read(ref _ms));

	public void Advance(TimeSpan by) => Interlocked.Add(ref _ms, (long)by.TotalMilliseconds);
}

/// <summary>Collects log events so tests can assert on what was logged.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CapturingSink : ILogEventSink
{
	private readonly ConcurrentQueue<LogEvent> _events = new();

	public IReadOnlyList<LogEvent> Events => _events.ToArray();

	public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);

	public IReadOnlyList<string> Messages(LogEventLevel level) =>
		Events.Where(e => e.Level == level).Select(e => e.RenderMessage()).ToList();

	public ILogger CreateLogger() => new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();
}

/// <summary>Dispatcher fake: posted work waits until the test plays the UI thread with <see cref="RunPending"/>.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class QueueingUiDispatcher : IUiDispatcher
{
	private readonly ConcurrentQueue<Action> _pending = new();

	public int PendingCount => _pending.Count;

	public void Post(Action action) => _pending.Enqueue(action);

	public Task InvokeAsync(Action action)
	{
		action();
		return Task.CompletedTask;
	}

	public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());

	public void RunPending()
	{
		while (_pending.TryDequeue(out var action))
			action();
	}
}

/// <summary>Inspector fake that counts captures and signals the first one.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeRuntimeInspector : IRuntimeInspector
{
	private int _stackCaptures;

	public int StackCaptures => Volatile.Read(ref _stackCaptures);
	public ManualResetEventSlim StackCaptured { get; } = new(false);
	public List<string> HistogramPaths { get; } = [];

	public ThreadStackCaptureModel CaptureThreadStacks()
	{
		Interlocked.Increment(ref _stackCaptures);
		StackCaptured.Set();
		return new ThreadStackCaptureModel(true, "--- managed thread 1 [UI]\n    Frame.A\n", Enumerable.Range(1, 20).Select(i => $"Frame.{i}").ToList());
	}

	public InspectionOutcomeModel WriteHeapHistogram(string path)
	{
		HistogramPaths.Add(path);
		File.WriteAllText(path, string.Join('\n', Enumerable.Range(1, 40).Select(i => $"line {i}")));
		return new InspectionOutcomeModel(true, path);
	}

	public InspectionOutcomeModel WriteDump(string path) => new(false, "fake: dumps disabled");
}

/// <summary>A temp diag folder; disposing deletes it.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class DiagnosticsTestEnv : IDisposable
{
	public string Folder { get; } = Path.Combine(Path.GetTempPath(), $"diag-tests-{Guid.NewGuid():N}");
	public DiagnosticsSettingsModel Settings { get; } = new();
	public StepTimeProvider Clock { get; } = new();
	public CapturingSink Sink { get; } = new();
	public DiagnosticsArtifactStore Store { get; }

	public DiagnosticsTestEnv()
	{
		Store = new DiagnosticsArtifactStore(Folder, Settings, Clock, Sink.CreateLogger());
	}

	public void Dispose()
	{
		// A background capture may still be writing its artifact when the test body is done: wait for it to let go.
		var deadline = Environment.TickCount64 + 10_000;
		while (Directory.Exists(Folder))
		{
			try
			{
				Directory.Delete(Folder, recursive: true);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				if (Environment.TickCount64 > deadline)
					throw;
				System.Threading.Thread.Sleep(25);
			}
		}
	}

	public static bool WaitFor(Func<bool> condition, int timeoutMs = 5000)
	{
		var deadline = Environment.TickCount64 + timeoutMs;
		while (Environment.TickCount64 < deadline)
		{
			if (condition())
				return true;
			Thread.Sleep(10);
		}
		return condition();
	}
}
