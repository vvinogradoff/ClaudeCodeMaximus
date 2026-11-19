using System.Windows;

namespace TessynDesktop.WPF.Views;

/// <summary>One shell panel as the docking area is given it (FR.6.11): its stable id, its title and its content. The Session view is not one: it is the central document.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellPanelDefinition
{
	/// <summary>One of the <see cref="Constants.DockedShell"/> panel ids.</summary>
	public required string Id { get; init; }

	public required string Title { get; init; }

	public required FrameworkElement Content { get; init; }

	/// <summary>True for the panels of the left pane (Sessions, Recent, Tags), false for Focus.</summary>
	public bool IsLeft { get; init; }
}
