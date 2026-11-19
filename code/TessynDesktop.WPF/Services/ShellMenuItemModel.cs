namespace TessynDesktop.WPF.Services;

/// <summary>One built menu entry; <see cref="BeginsGroup"/> asks the view for a separator before it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellMenuItemModel
{
	public required string CommandId { get; init; }

	public required string Title { get; init; }

	/// <summary>The effective gesture (user override or default), for display; null = none.</summary>
	public string? Gesture { get; init; }

	public bool IsEnabled { get; init; }

	public bool BeginsGroup { get; init; }

	/// <summary>The command shows a check mark (a panel that is open, FR.6.14); false for the usual entry.</summary>
	public bool IsChecked { get; init; }
}
