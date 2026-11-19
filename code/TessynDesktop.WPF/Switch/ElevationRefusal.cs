namespace TessynDesktop.WPF.Switch;

/// <summary>An elevated start of the host that did not happen (FR.32.13): the User declined the administrator prompt, or the start failed (<paramref name="Reason"/>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ElevationRefusal(bool Declined, string? Reason)
{
	/// <summary>The notice shown while the daemon then runs normally.</summary>
	public string NoticeText => Declined
		? Constants.Switch.ElevationDeclinedText
		: string.Format(System.Globalization.CultureInfo.InvariantCulture, Constants.Switch.ElevationFailedFormat, Reason ?? Constants.Switch.ElevationUnknownReason);
}
