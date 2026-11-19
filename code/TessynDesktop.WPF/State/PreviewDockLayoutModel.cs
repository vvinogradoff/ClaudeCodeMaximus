namespace TessynDesktop.WPF.State;

/// <summary>What is remembered about one group's docked panel (FR.29.21): where it sits, its size, auto-hide, floating, shown.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewDockLayoutModel
{
	public PreviewDockSide Side { get; set; } = PreviewDockSide.Right;

	/// <summary>Group ids of the panels it shared a pane (tab strip) with; empty when it had its own pane.</summary>
	public List<string> StackedWith { get; set; } = [];

	/// <summary>Its position among the tabs of its pane.</summary>
	public int TabIndex { get; set; }

	/// <summary>Width (left, right) or height (top, bottom) of its pane in device-independent pixels; 0 = the first-time size (FR.29.21).</summary>
	public double Size { get; set; }

	public bool AutoHide { get; set; }

	public bool Floating { get; set; }

	/// <summary>The floating window's placement (FR.29.16 rules) while <see cref="Floating"/>.</summary>
	public PreviewPlacementModel? FloatingPlacement { get; set; }

	/// <summary>False once the user closed the panel; its next item opens it again at this layout.</summary>
	public bool Shown { get; set; } = true;

	public PreviewDockLayoutModel Clone() => new()
	{
		Side = Side,
		StackedWith = [.. StackedWith],
		TabIndex = TabIndex,
		Size = Size,
		AutoHide = AutoHide,
		Floating = Floating,
		FloatingPlacement = FloatingPlacement,
		Shown = Shown,
	};
}