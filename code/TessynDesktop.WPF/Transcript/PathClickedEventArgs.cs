namespace TessynDesktop.WPF.Transcript;

/// <summary>A local-path chip under an assistant entry was clicked (FR.26.5).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PathClickedEventArgs : EventArgs
{
	public PathClickedEventArgs(int entryIndex, string path)
	{
		EntryIndex = entryIndex;
		Path = path;
	}

	/// <summary>Index of the entry the chip belongs to.</summary>
	public int EntryIndex { get; }

	/// <summary>The path exactly as it occurs in the entry's text (validated by the receiver).</summary>
	public string Path { get; }
}
