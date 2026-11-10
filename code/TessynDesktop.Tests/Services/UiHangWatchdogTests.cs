using System;
using System.IO;
using System.Linq;
using Serilog.Events;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>D1.4: stall onset, recovery, one capture per stall and the capture cooldown, with a fake clock and dispatcher.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class UiHangWatchdogTests : IDisposable
{
	private const int StepMs = 250;

	private readonly DiagnosticsTestEnv _env = new();
	private readonly QueueingUiDispatcher _dispatcher = new();
	private readonly FakeRuntimeInspector _inspector = new();
	private readonly UiThreadTracker _ui = new();
	private readonly OperationTimer _operations;
	private readonly UiHangWatchdog _watchdog;

	public UiHangWatchdogTests()
	{
		_ui.CaptureCurrentThread();
		_operations = new OperationTimer(_ui, _env.Clock, _env.Sink.CreateLogger());
		_watchdog = new UiHangWatchdog(_env.Settings, _dispatcher, _inspector, _env.Store, _operations, _ui, _env.Clock, _env.Sink.CreateLogger());
	}

	public void Dispose() => _env.Dispose();

	/// <summary>Lets <paramref name="ms"/> of fake time pass in 250 ms heartbeat steps with the UI thread not answering.</summary>
	private void StallFor(int ms)
	{
		for (var elapsed = 0; elapsed < ms; elapsed += StepMs)
		{
			_env.Clock.Advance(TimeSpan.FromMilliseconds(StepMs));
			_watchdog.Tick();
		}
	}

	/// <summary>The UI thread comes back; the next tick notices.</summary>
	private void Recover()
	{
		_dispatcher.RunPending();
		_env.Clock.Advance(TimeSpan.FromMilliseconds(StepMs));
		_watchdog.Tick();
	}

	private int Count(string startsWith) =>
		_env.Sink.Messages(LogEventLevel.Warning).Count(m => m.StartsWith(startsWith, StringComparison.Ordinal));

	[Fact]
	public void Tick_ResponsiveUi_NeverWarns()
	{
		for (var i = 0; i < 20; i++)
		{
			_watchdog.Tick();
			_dispatcher.RunPending();
			_env.Clock.Advance(TimeSpan.FromMilliseconds(StepMs));
		}

		Assert.Empty(_env.Sink.Events);
		Assert.False(_watchdog.GetState().StalledNow);
	}

	[Fact]
	public void Tick_StallReachesWarnThreshold_LogsOneOnsetWarningWithCurrentOperation()
	{
		var scope = _operations.Measure(Constants.Diagnostics.Operation.SessionSwitch, int.MaxValue);
		_watchdog.Tick();

		StallFor(750);
		Assert.Equal(0, Count("UI-HANG no heartbeat"));

		StallFor(250);
		Assert.Equal(1, Count("UI-HANG no heartbeat"));
		Assert.Contains("session-switch", _env.Sink.Messages(LogEventLevel.Warning).Single(m => m.StartsWith("UI-HANG no heartbeat")));
		Assert.True(_watchdog.GetState().StalledNow);
		Assert.Equal(1000, _watchdog.GetState().CurrentStallMs);

		StallFor(1500);
		Assert.Equal(1, Count("UI-HANG no heartbeat"));
		scope.Dispose();
	}

	[Fact]
	public void Tick_UiAnswersAfterStall_LogsRecoveryWithDurationAndRecordsLastStall()
	{
		var scope = _operations.Measure(Constants.Diagnostics.Operation.ResumabilitySweep, int.MaxValue);
		_watchdog.Tick();
		StallFor(1500);

		Recover();

		var recovered = Assert.Single(_env.Sink.Messages(LogEventLevel.Warning), m => m.StartsWith("UI-HANG recovered"));
		Assert.Contains("after 1500ms", recovered);
		Assert.Contains("resumability-sweep", recovered);
		Assert.Contains("gen2 +", recovered);
		Assert.Contains("gc pause +", recovered);

		var state = _watchdog.GetState();
		Assert.False(state.StalledNow);
		Assert.NotNull(state.LastStall);
		Assert.Equal(1500, state.LastStall!.DurationMs);
		Assert.Equal(Constants.Diagnostics.Operation.ResumabilitySweep, state.LastStall.Operation);
		scope.Dispose();
	}

	[Fact]
	public void Tick_StallBelowWarnThreshold_IsNotReported()
	{
		_watchdog.Tick();
		StallFor(750);

		Recover();

		Assert.Empty(_env.Sink.Events);
		Assert.Null(_watchdog.GetState().LastStall);
	}

	[Fact]
	public void Tick_StallReachesCaptureThreshold_CapturesStacksOncePerStall()
	{
		_watchdog.Tick();
		StallFor(4750);
		Assert.Equal(0, _inspector.StackCaptures);

		StallFor(250);
		Assert.True(_inspector.StackCaptured.Wait(5000));
		Assert.True(DiagnosticsTestEnv.WaitFor(() => Count("UI-HANG stacks written") == 1));

		StallFor(5000);
		Assert.Equal(1, _inspector.StackCaptures);

		var file = Assert.Single(Directory.GetFiles(_env.Folder, "hang-*.txt"));
		var text = File.ReadAllText(file);
		Assert.Contains("[UI]", text);
		Assert.Contains("Stalled for at least 5000 ms", text);

		var logged = _env.Sink.Messages(LogEventLevel.Warning).Single(m => m.StartsWith("UI-HANG stacks written"));
		Assert.Contains(file, logged);
		Assert.Contains("Frame.15", logged);
		Assert.DoesNotContain("Frame.16", logged);
	}

	[Fact]
	public void Tick_SecondStallWithinCooldown_DoesNotCaptureAgainButStallAfterCooldownDoes()
	{
		_watchdog.Tick();
		StallFor(5000);
		Assert.True(_inspector.StackCaptured.Wait(5000));

		Recover();
		StallFor(6000);
		Assert.Contains(_env.Sink.Messages(LogEventLevel.Information), m => m.Contains("cooldown"));
		System.Threading.Thread.Sleep(100);
		Assert.Equal(1, _inspector.StackCaptures);

		Recover();
		_dispatcher.RunPending();
		_env.Clock.Advance(TimeSpan.FromMinutes(_env.Settings.HangCaptureCooldownMinutes + 1));
		_watchdog.Tick();
		StallFor(5000);

		Assert.True(DiagnosticsTestEnv.WaitFor(() => _inspector.StackCaptures == 2));
	}
}
