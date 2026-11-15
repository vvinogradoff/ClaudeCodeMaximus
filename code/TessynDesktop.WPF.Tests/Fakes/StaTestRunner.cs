using System.Runtime.ExceptionServices;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Runs an action on a fresh STA thread (WPF elements need one) and rethrows its failure.</summary>
/// <remarks>Created by Claude</remarks>
public static class StaTestRunner
{
	public static void Run(Action action)
	{
		Exception? failure = null;
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
		if (failure != null)
			ExceptionDispatchInfo.Capture(failure).Throw();
	}
}
