namespace TessynDesktop.WPF.Services;

/// <summary>The preview page reports that it showed (or failed to show) the content it was given.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewRenderedEventArgs : EventArgs
{
	public PreviewRenderedEventArgs(string kind, bool ok)
	{
		Kind = kind;
		Ok = ok;
	}

	/// <summary>"text", "markdown", "image" or "video".</summary>
	public string Kind { get; }

	public bool Ok { get; }
}
