using System;
using System.Threading;
using Serilog.Events;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>D1.5 infrastructure: <see cref="OperationTimer"/> threshold, nesting and UI-thread scoping.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class OperationTimerTests
{
	private readonly StepTimeProvider _clock = new();
	private readonly CapturingSink _sink = new();
	private readonly UiThreadTracker _ui = new();
	private readonly OperationTimer _timer;

	public OperationTimerTests()
	{
		_ui.CaptureCurrentThread();
		_timer = new OperationTimer(_ui, _clock, _sink.CreateLogger());
	}

	[Fact]
	public void Measure_BelowThreshold_LogsNothing()
	{
		using (_timer.Measure(Constants.Diagnostics.Operation.SessionSwitch, 100))
			_clock.Advance(TimeSpan.FromMilliseconds(99));

		Assert.Empty(_sink.Events);
	}

	[Fact]
	public void Measure_AtThreshold_LogsSlowUiWarning()
	{
		using (_timer.Measure(Constants.Diagnostics.Operation.SessionSwitch, 100))
			_clock.Advance(TimeSpan.FromMilliseconds(100));

		var warning = Assert.Single(_sink.Messages(LogEventLevel.Warning));
		Assert.StartsWith("SLOW-UI session-switch 100ms (gen2 +", warning);
		Assert.Contains("gc pause +", warning);
	}

	[Fact]
	public void Measure_Nested_RestoresPreviousOperationOnDispose()
	{
		Assert.Null(_timer.CurrentOperation);

		var outer = _timer.Measure("outer", 1000);
		Assert.Equal("outer", _timer.CurrentOperation);

		var inner = _timer.Measure("inner", 1000);
		Assert.Equal("inner", _timer.CurrentOperation);

		inner.Dispose();
		Assert.Equal("outer", _timer.CurrentOperation);

		outer.Dispose();
		Assert.Null(_timer.CurrentOperation);
	}

	[Fact]
	public void Measure_OnBackgroundThread_DoesNotPublishCurrentOperation()
	{
		string? seenOnBackground = "unset";
		var thread = new Thread(() =>
		{
			using var scope = _timer.Measure("background", 1000);
			seenOnBackground = _timer.CurrentOperation;
		});
		thread.Start();
		thread.Join();

		Assert.Null(seenOnBackground);
	}
}
