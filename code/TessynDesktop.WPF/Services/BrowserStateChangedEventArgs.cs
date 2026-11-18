namespace TessynDesktop.WPF.Services;

/// <summary>The browser's address, title, history or loading state changed.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserStateChangedEventArgs : EventArgs
{
	public BrowserStateChangedEventArgs(bool isLoading)
	{
		IsLoading = isLoading;
	}

	/// <summary>True while a top-level navigation is in progress.</summary>
	public bool IsLoading { get; }
}
