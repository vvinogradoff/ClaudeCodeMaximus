namespace TessynDesktop.WPF.Services;

/// <summary>
/// Which frame navigations the preview page allows: only the one <c>srcdoc</c> load that each html preview starts (FR.26.8). A link, a form
/// or a refresh meta in the page never gets through, and a refresh to <c>about:srcdoc</c> is refused because the expected load was consumed.
/// One instance per surface: it holds the count of expected loads.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FramePolicy
{
	private int _expected;

	/// <summary>An html preview was started: exactly one srcdoc load is now allowed (a second start before the first load replaces it, it does not add one).</summary>
	public void ExpectSrcdocLoad() => _expected = 1;

	/// <summary>The preview is no longer an html page: no srcdoc load is expected.</summary>
	public void ExpectNothing() => _expected = 0;

	/// <summary>True only for the address of a srcdoc document, and only once per <see cref="ExpectSrcdocLoad"/>.</summary>
	public bool IsAllowed(string? uri)
	{
		if (!string.Equals(uri, Constants.Hosts.SrcdocUri, StringComparison.OrdinalIgnoreCase) || _expected == 0)
			return false;

		_expected = 0;
		return true;
	}
}
