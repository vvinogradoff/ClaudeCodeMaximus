namespace TessynDesktop.WPF.State;

/// <summary>A preview window that is open, saved so that the next start reopens it with its item (FR.29.11).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class OpenPreviewWindowModel
{
	public string GroupId { get; set; } = string.Empty;

	public bool Pinned { get; set; }

	public PreviewItemModel Item { get; set; } = new();

	/// <summary>Pinned windows only: their own last placement, to reopen them there (never the group's placement, FR.29.8).</summary>
	public PreviewPlacementModel? Placement { get; set; }
}
