using System.Runtime.ExceptionServices;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>
/// Runs an action on a fresh STA thread (WPF elements need one) and rethrows its failure. Runs are serialized process-wide: test classes
/// run in parallel, and WPF is not safe for several threads loading the same component at once (the shared BAML resource part throws
/// "Index was out of range" in <c>PackagePart.GetStream</c>), nor for several views feeding the process-wide binding trace source.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class StaTestRunner
{
	private static readonly object Gate = new();

	public static void Run(Action action)
	{
		Exception? failure = null;
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
			});
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			thread.Join();
		}

		if (failure != null)
			ExceptionDispatchInfo.Capture(failure).Throw();
	}
}
