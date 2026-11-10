namespace TessynDesktop.Services;

/// <summary>Remembers the managed thread id of the UI thread, so logging, the watchdog and the inspector can tell it apart.</summary>
/// <remarks>Created by Claude</remarks>
public interface IUiThreadTracker
{
	/// <summary>Managed thread id of the UI thread, or null until captured.</summary>
	int? UiThreadId { get; }

	/// <summary>Records the calling thread as the UI thread.</summary>
	void CaptureCurrentThread();

	/// <summary>True when the calling thread is the captured UI thread.</summary>
	bool IsUiThread();
}
