namespace TessynDesktop.WPF.Services;

/// <summary>Which frame navigations the preview page allows: only the html frame's own <c>srcdoc</c> load (FR.26.8). A link, a refresh meta or a form in the page never gets through.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FramePolicy
{
	/// <summary>True only for the address of a srcdoc document.</summary>
	public bool IsAllowed(string? uri) => string.Equals(uri, Constants.Hosts.SrcdocUri, StringComparison.OrdinalIgnoreCase);
}
