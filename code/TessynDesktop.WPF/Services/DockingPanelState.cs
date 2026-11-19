namespace TessynDesktop.WPF.Services;

/// <summary>
/// One panel as the docking test hook's <c>state</c> reply reports it (FR.6.22): where it is (side, pane, tab index, selected), how big its pane is, auto-hide and edge,
/// the floating rectangle, whether it is shown, and for Focus the badge text. <see cref="Pane"/> is the index of the pane in layout order (panels with the same value share
/// a tab strip); -1 when the panel is not in the layout.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record DockingPanelState
{
	public required string Id { get; init; }

	/// <summary>shell, preview or session.</summary>
	public required string Kind { get; init; }

	public bool Shown { get; init; }

	/// <summary>left, right, top, bottom (of the session area), center, floating or closed.</summary>
	public string Side { get; init; } = string.Empty;

	public int Pane { get; init; } = -1;

	public int TabIndex { get; init; }

	public bool Selected { get; init; }

	/// <summary>Width of the panel's pane (its slide-out width while auto-hidden), device-independent pixels.</summary>
	public double Width { get; init; }

	public double Height { get; init; }

	public bool AutoHide { get; init; }

	/// <summary>The edge an auto-hidden panel sits at: left, right, top or bottom.</summary>
	public string? Edge { get; init; }

	public bool Floating { get; init; }

	public DockingFloatRect? FloatRect { get; init; }

	/// <summary>The Focus tab's badge text (the open count) while it is shown; null for the other panels or without a badge.</summary>
	public string? Badge { get; init; }
}
