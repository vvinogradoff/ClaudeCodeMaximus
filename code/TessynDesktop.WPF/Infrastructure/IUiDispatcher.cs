namespace TessynDesktop.WPF.Infrastructure;

/// <summary>Marshals work onto the UI thread so view models stay free of WPF types.</summary>
/// <remarks>Created by Claude</remarks>
public interface IUiDispatcher
{
	/// <summary>True when the caller already runs on the UI thread.</summary>
	bool CheckAccess();

	/// <summary>Queues <paramref name="action"/> on the UI thread; runs it inline when already there.</summary>
	void Post(Action action);
}
