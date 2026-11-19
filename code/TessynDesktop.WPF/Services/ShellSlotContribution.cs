using System.Windows;

namespace TessynDesktop.WPF.Services;

/// <summary>A view a package puts into a layout slot (<see cref="Constants.Slots"/>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellSlotContribution
{
	public required string SlotId { get; init; }

	/// <summary>Creates the view when the slot is shown; it inherits the slot's DataContext unless it sets its own.</summary>
	public required Func<UIElement> Create { get; init; }

	/// <summary>Left to right (or top to bottom) among the contributions of the slot; equal orders keep registration order.</summary>
	public int Order { get; init; }

	/// <summary>The tab header; only used by the <see cref="Constants.Slots.LeftPanelTabs"/> slot.</summary>
	public string? Title { get; init; }

	/// <summary>The stable panel id of a left-panel contribution in the docked shell (FR.6.11), e.g. <see cref="Constants.DockedShell.RecentPanelId"/>; only used by the <see cref="Constants.Slots.LeftPanelTabs"/> slot.</summary>
	public string? PanelId { get; init; }
}
