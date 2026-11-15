namespace TessynDesktop.WPF.Services;

/// <summary>An action the shell can run by name from a key gesture or a menu entry.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellCommand
{
	/// <summary>Stable id (<c>area.verb</c>); key binding overrides and menus refer to it.</summary>
	public required string Id { get; init; }

	/// <summary>The menu text.</summary>
	public required string Title { get; init; }

	/// <summary>Default key gesture in WPF notation (<c>Ctrl+Shift+N</c>, <c>F2</c>); null = no key.</summary>
	public string? DefaultGesture { get; init; }

	/// <summary>Runs the command; the parameter is what the caller offers (a tree node for the tree menu, null for a key).</summary>
	public required Func<object?, Task> ExecuteAsync { get; init; }

	/// <summary>Whether the command applies to the parameter; null = always.</summary>
	public Func<object?, bool>? CanExecute { get; init; }
}
