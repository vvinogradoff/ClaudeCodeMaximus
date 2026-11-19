namespace TessynDesktop.WPF.State;

/// <summary>A docked panel that is open, saved so that the next start reopens it with its item (FR.29.22).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class OpenPreviewPanelModel
{
	/// <summary>The group id, or <see cref="Constants.Previews.DefaultPanelId"/> for the default Preview panel.</summary>
	public string GroupId { get; set; } = string.Empty;

	public bool Pinned { get; set; }

	public PreviewItemModel Item { get; set; } = new();

	/// <summary>Pinned panels only: their own last layout (never the group's, FR.29.20).</summary>
	public PreviewDockLayoutModel? Layout { get; set; }

	/// <summary>Pinned panels only: the content id the panel had, reused after a restart so the saved neighbours (<c>StackedWith</c>) still match; null in data saved before the id was kept.</summary>
	public string? PanelId { get; set; }
}