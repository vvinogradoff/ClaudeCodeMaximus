using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>
/// Runs an action on a fresh STA thread (WPF elements need one) and rethrows its failure. Runs are serialized process-wide: test classes
/// run in parallel, and WPF is not safe for several threads loading the same component at once (the shared BAML resource part throws
/// "Index was out of range" in <c>PackagePart.GetStream</c>), nor for several views feeding the process-wide binding trace source.
/// <para>
/// Two guards against a hung run. (1) The process-wide <c>Application</c> is created by <see cref="EnsureApplication"/> on its own STA thread
/// that keeps pumping its dispatcher. A test used to create the <c>Application</c> on its short-lived test thread; <c>Application.Current</c>
/// then outlived the thread, and the first <c>DockingManager</c> loaded afterwards waited forever in AvalonDock's
/// <c>FocusElementManager.SetupFocusManagement</c> (<c>Application.Current.Dispatcher.Invoke</c> on a dispatcher nobody pumps). Shutting that
/// dispatcher down instead does not help: the <c>Invoke</c> then throws <c>TaskCanceledException</c> inside the Loaded handler and crashes the host.
/// (2) A run that does not finish within <see cref="TimeLimit"/> fails with a message naming the hang instead of blocking every other test behind the lock.
/// </para>
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class StaTestRunner
{
	/// <summary>The longest one run may take; a healthy WPF test needs seconds.</summary>
	public static readonly TimeSpan TimeLimit = TimeSpan.FromMinutes(2);

	private static readonly object Gate = new();
	private static readonly object ApplicationGate = new();
	private static Application? _application;

	/// <summary>The process-wide <c>Application</c> (explicit shutdown), created once on a background STA thread whose dispatcher keeps pumping. Tests that need an application use this instead of creating one.</summary>
	public static Application EnsureApplication()
	{
		lock (ApplicationGate)
		{
			if (_application != null)
				return _application;

			var created = new ManualResetEventSlim();
			Application? application = null;
			var thread = new Thread(() =>
			{
				application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
				created.Set();
				Dispatcher.Run();
			})
			{
				IsBackground = true,
				Name = "TestApplication"
			};
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			if (!created.Wait(TimeLimit))
				throw new TimeoutException("The test Application was not created in time.");

			_application = application!;
			return _application;
		}
	}

	public static void Run(Action action) => Run(action, TimeLimit);

	public static void Run(Action action, TimeSpan timeLimit)
	{
		Exception? failure = null;
		var finished = false;
		lock (Gate)
		{
			var thread = new Thread(() =>
			{
				try
				{
					action();
				}
				catch (Exception ex)
				{
					failure = ex;
				}
			})
			{
				IsBackground = true   // a hung thread must not keep the test host alive
			};
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			finished = thread.Join(timeLimit);
		}

		if (!finished)
			throw new TimeoutException($"The STA test run did not finish within {timeLimit}: the test thread is hung (typically waiting on a dispatcher that no longer pumps, e.g. Application.Current.Dispatcher.Invoke from AvalonDock's FocusElementManager).");

		if (failure != null)
			ExceptionDispatchInfo.Capture(failure).Throw();
	}
}
