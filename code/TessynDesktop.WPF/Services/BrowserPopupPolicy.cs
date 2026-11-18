namespace TessynDesktop.WPF.Services;

/// <summary>
/// Decides whether a page's request for a new window opens one, and whether it may take the focus (FR.29.5, FR.29.11): only a user-initiated request may activate
/// the popup, and one browser surface keeps at most <see cref="Constants.Browser.MaxPopups"/> popups. Pure.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class BrowserPopupPolicy
{
	private readonly int _maxPopups;

	public BrowserPopupPolicy(int maxPopups = Constants.Browser.MaxPopups)
	{
		_maxPopups = maxPopups;
	}

	public BrowserPopupDecision Decide(int openPopups, bool userInitiated)
	{
		if (openPopups >= _maxPopups)
			return BrowserPopupDecision.Drop;

		return userInitiated ? BrowserPopupDecision.ShowActive : BrowserPopupDecision.ShowInactive;
	}
}
