namespace TessynDesktop.WPF.Transcript;

/// <summary>Where the transcript page is scrolled to (reported after the user scrolled, for the Back control).</summary>
/// <param name="TopIndex">Index of the entry at the top of the view.</param>
/// <param name="AtBottom">The view follows the newest entry.</param>
/// <remarks>Created by Claude</remarks>
public sealed class ViewAnchorEventArgs : EventArgs
{
	public ViewAnchorEventArgs(int topIndex, bool atBottom)
	{
		TopIndex = topIndex;
		AtBottom = atBottom;
	}

	public int TopIndex { get; }

	public bool AtBottom { get; }
}
