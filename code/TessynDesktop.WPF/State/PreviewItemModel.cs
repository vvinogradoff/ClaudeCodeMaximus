namespace TessynDesktop.WPF.State;

/// <summary>
/// One previewed item (FR.29.4, FR.29.11): what a preview window shows, where it came from (auto-close, FR.29.9) and how to open it again after a
/// restart. A file is never stored as a read handle: it is opened again from its Focus reference (<see cref="RefId"/>, valid across restarts) or by
/// asking <c>file.preview</c> again for <see cref="NodeId"/>, <see cref="EntryId"/> and <see cref="Path"/>, so the daemon's checks run again.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewItemModel
{
	public PreviewItemKind Kind { get; set; }

	/// <summary>Url: the address; kept current while the page navigates, so a restart reopens the page that was shown.</summary>
	public string? Url { get; set; }

	/// <summary>File: the file name for the title and for matching.</summary>
	public string? FileName { get; set; }

	/// <summary>File from a Focus file reference: its <c>refId</c>.</summary>
	public string? RefId { get; set; }

	/// <summary>File from a transcript path: the session.</summary>
	public string? NodeId { get; set; }

	/// <summary>File from a transcript path: the entry.</summary>
	public string? EntryId { get; set; }

	/// <summary>File from a transcript path: the path as written in the entry.</summary>
	public string? Path { get; set; }

	/// <summary>The session the main window showed when the item was opened (auto-close rule 1).</summary>
	public string? SourceNodeId { get; set; }

	/// <summary>The Focus item whose reference opened it (auto-close rule 2); null otherwise.</summary>
	public string? SourceFocusItemId { get; set; }

	public PreviewItemModel Clone() => (PreviewItemModel)MemberwiseClone();
}
