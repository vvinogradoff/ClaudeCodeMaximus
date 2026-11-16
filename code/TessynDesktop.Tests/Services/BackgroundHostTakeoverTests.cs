using System.Diagnostics;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// Side-by-side, WPF first: a visible Avalonia window takes over from a BACKGROUND host through the named exit event, and from nothing else. Every test uses its own names, never the real mutex or event.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class BackgroundHostTakeoverTests
{
	private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);

	private static string Unique(string kind) => @"Local\TessynDesktop.Test." + kind + "." + Guid.NewGuid().ToString("N");

	/// <summary>A stand-in background host: holds the guard on its own thread (a mutex belongs to its thread), listens on the exit event, releases the guard when asked.</summary>
	private sealed class FakeBackgroundHost : IDisposable
	{
		private readonly Thread _thread;
		private readonly ManualResetEventSlim _ready = new();
		private readonly ManualResetEventSlim _asked = new();
		private readonly ManualResetEventSlim _stop = new();
		private readonly TimeSpan _delayBeforeListening;
		private readonly bool _leaves;

		public FakeBackgroundHost(string guardName, string eventName, bool leaves = true, TimeSpan? delayBeforeListening = null)
		{
			_leaves = leaves;
			_delayBeforeListening = delayBeforeListening ?? TimeSpan.Zero;
			_thread = new Thread(() => Run(guardName, eventName)) { IsBackground = true };
			_thread.Start();
			Assert.True(_ready.Wait(TimeSpan.FromSeconds(10)));
		}

		public bool WasAsked => _asked.IsSet;

		private void Run(string guardName, string eventName)
		{
			using var guard = new DaemonHostGuard(guardName);
			Assert.True(guard.TryAcquire());
			_ready.Set();
			if (_delayBeforeListening > TimeSpan.Zero)
				Thread.Sleep(_delayBeforeListening);

			using var listener = new DaemonHostExitListener(eventName);
			listener.Start(() => _asked.Set());
			WaitHandle.WaitAny([_asked.WaitHandle, _stop.WaitHandle]);
			if (_asked.IsSet && !_leaves)
				_stop.Wait(); // asked, but it hangs on to the guard
		}

		public void Dispose()
		{
			_stop.Set();
			_thread.Join(TimeSpan.FromSeconds(10));
		}
	}

	/// <summary>A stand-in visible window: holds the guard, never creates the exit event.</summary>
	private sealed class FakeVisibleWindow : IDisposable
	{
		private readonly Thread _thread;
		private readonly ManualResetEventSlim _ready = new();
		private readonly ManualResetEventSlim _stop = new();

		public FakeVisibleWindow(string guardName)
		{
			_thread = new Thread(() =>
			{
				using var guard = new DaemonHostGuard(guardName);
				Assert.True(guard.TryAcquire());
				_ready.Set();
				_stop.Wait();
			}) { IsBackground = true };
			_thread.Start();
			Assert.True(_ready.Wait(TimeSpan.FromSeconds(10)));
		}

		public void Dispose()
		{
			_stop.Set();
			_thread.Join(TimeSpan.FromSeconds(10));
		}
	}

	[Fact]
	public void ABackgroundHost_IsAskedToExit_AndTheWindowTakesTheGuard()
	{
		var guardName = Unique("guard");
		var eventName = Unique("exit");
		using var host = new FakeBackgroundHost(guardName, eventName);
		using var window = new DaemonHostGuard(guardName);
		Assert.False(window.TryAcquire()); // what Program.Main sees first

		var outcome = BackgroundHostTakeover.TryTakeOver(window, eventName, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10), Poll);

		Assert.Equal(TakeoverOutcome.Acquired, outcome);
		Assert.True(host.WasAsked);
		Assert.True(window.TryAcquire());
	}

	[Fact]
	public void AVisibleWindow_IsNeverAskedToExit_AndNothingIsAcquired()
	{
		var guardName = Unique("guard");
		using var visible = new FakeVisibleWindow(guardName); // holds the guard and, like the real window, creates no exit event
		using var second = new DaemonHostGuard(guardName);
		var watch = Stopwatch.StartNew();

		var outcome = BackgroundHostTakeover.TryTakeOver(second, Unique("exit"), TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(10), Poll);

		Assert.Equal(TakeoverOutcome.NotABackgroundHost, outcome);
		Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "it must not wait for the long exit timeout when nothing was asked");
		Assert.True(DaemonHostGuard.IsHeldElsewhere(guardName)); // the window is undisturbed
	}

	[Fact]
	public void ABackgroundHostThatDoesNotLeave_EndsAsDidNotExit()
	{
		var guardName = Unique("guard");
		var eventName = Unique("exit");
		using var host = new FakeBackgroundHost(guardName, eventName, leaves: false);
		using var window = new DaemonHostGuard(guardName);

		var outcome = BackgroundHostTakeover.TryTakeOver(window, eventName, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(400), Poll);

		Assert.Equal(TakeoverOutcome.DidNotExit, outcome);
		Assert.True(host.WasAsked);
	}

	[Fact]
	public void AHostStillCreatingItsEvent_IsFoundByTheRetry()
	{
		var guardName = Unique("guard");
		var eventName = Unique("exit");
		using var host = new FakeBackgroundHost(guardName, eventName, delayBeforeListening: TimeSpan.FromMilliseconds(400));
		using var window = new DaemonHostGuard(guardName);

		var outcome = BackgroundHostTakeover.TryTakeOver(window, eventName, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), Poll);

		Assert.Equal(TakeoverOutcome.Acquired, outcome);
		Assert.True(host.WasAsked);
	}

	[Fact]
	public void AFreeGuard_IsTakenWithoutAskingAnyone()
	{
		var eventName = Unique("exit");
		using var spy = new DaemonHostExitListener(eventName);
		var asked = new ManualResetEventSlim();
		spy.Start(() => asked.Set());
		using var window = new DaemonHostGuard(Unique("guard"));

		var outcome = BackgroundHostTakeover.TryTakeOver(window, eventName, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), Poll);

		Assert.Equal(TakeoverOutcome.Acquired, outcome);
		Assert.False(asked.Wait(200));
	}

	[Fact]
	public async Task ABackgroundHostInAnotherProcess_IsTakenOverFrom()
	{
		var guardName = Unique("guard");
		var eventName = Unique("exit");
		var marker = Path.Combine(Path.GetTempPath(), "tessyn-takeover-" + Guid.NewGuid().ToString("N"));
		// The script is the background host: guard + exit event, leaves when the event is set (its mutex is released by the same thread).
		var script = $"$m = New-Object System.Threading.Mutex($true, '{guardName}'); $e = New-Object System.Threading.EventWaitHandle($false, 'AutoReset', '{eventName}'); New-Item -ItemType File -Path '{marker}' | Out-Null; if ($e.WaitOne(30000)) {{ $m.ReleaseMutex() }}";
		using var holder = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -Command \"{script}\"") { UseShellExecute = false, CreateNoWindow = true })!;
		try
		{
			var deadline = DateTime.UtcNow.AddSeconds(30);
			while (!File.Exists(marker) && DateTime.UtcNow < deadline)
				await Task.Delay(50);

			Assert.True(File.Exists(marker), "the host process did not start in time");
			using var window = new DaemonHostGuard(guardName);
			Assert.False(window.TryAcquire());

			var outcome = BackgroundHostTakeover.TryTakeOver(window, eventName, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(20), Poll);

			Assert.Equal(TakeoverOutcome.Acquired, outcome);
		}
		finally
		{
			if (!holder.HasExited)
				holder.Kill(entireProcessTree: true);
			holder.WaitForExit();
			File.Delete(marker);
		}
	}
}
