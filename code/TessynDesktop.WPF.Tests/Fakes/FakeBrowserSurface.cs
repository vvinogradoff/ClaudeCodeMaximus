using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>A scripted <see cref="IBrowserSurface"/>: records calls, keeps only the newest URL until "attached", and raises events on demand.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeBrowserSurface : IBrowserSurface
{
	public event EventHandler<BrowserStateChangedEventArgs>? StateChanged;

	public event EventHandler<string>? NavigationFailed;

	public event EventHandler? BrowserLost;

	public bool CanGoBack { get; set; }

	public bool CanGoForward { get; set; }

	public string? Source { get; set; }

	public string? DocumentTitle { get; set; }

	public bool Attached { get; private set; }

	public string? PendingUrl { get; private set; }

	public List<string> Navigated { get; } = [];

	public List<string> Calls { get; } = [];

	public int StateSubscribers => StateChanged?.GetInvocationList().Length ?? 0;

	public int FailedSubscribers => NavigationFailed?.GetInvocationList().Length ?? 0;

	public int LostSubscribers => BrowserLost?.GetInvocationList().Length ?? 0;

	public void Navigate(string url)
	{
		if (!Attached)
		{
			PendingUrl = url;
			return;
		}

		Navigated.Add(url);
	}

	public void Attach()
	{
		Attached = true;
		if (PendingUrl != null)
			Navigated.Add(PendingUrl);

		PendingUrl = null;
	}

	public void GoBack() => Calls.Add("back");

	public void GoForward() => Calls.Add("forward");

	public void Reload() => Calls.Add("reload");

	public void Stop() => Calls.Add("stop");

	public void RaiseState(bool isLoading) => StateChanged?.Invoke(this, new BrowserStateChangedEventArgs(isLoading));

	public void RaiseFailed(string status) => NavigationFailed?.Invoke(this, status);

	public void RaiseLost() => BrowserLost?.Invoke(this, EventArgs.Empty);
}
