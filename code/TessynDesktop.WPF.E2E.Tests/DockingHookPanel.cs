namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>One panel of the docking test hook's state reply (FR.6.22): side, pane, tab index, selected, size of its pane, auto-hide and edge, floating rectangle, shown, Focus badge.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed record DockingHookPanel(string Id, string Kind, bool Shown, string Side, int Pane, int TabIndex, bool Selected, double Width, double Height, bool AutoHide, string? Edge, bool Floating, DockingHookRect? FloatRect, string? Badge, bool SlidOut = false)
{
	/// <summary>The panel shares a tab strip with <paramref name="other"/>.</summary>
	public bool SharesPaneWith(DockingHookPanel other) => Shown && other.Shown && Pane >= 0 && Pane == other.Pane;
}
