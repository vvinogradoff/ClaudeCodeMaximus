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

	/// <summary>Runs the command; the parameter is what the caller offers (a tree node for the tree menu; for a key, <see cref="ResolveParameter"/>, else null).</summary>
	public required Func<object?, Task> ExecuteAsync { get; init; }

	/// <summary>Whether the command applies to the parameter; null = always.</summary>
	public Func<object?, bool>? CanExecute { get; init; }

	/// <summary>Whether the menu entry shows a check mark for the parameter (the Panels entries while their panel is open); null = never.</summary>
	public Func<object?, bool>? IsChecked { get; init; }

	/// <summary>
	/// Supplies the parameter when the command is run from its key gesture (e.g. the selected tree node), so a command bound to both a
	/// menu and a key sees the same kind of parameter. Null = the key passes null, and the command has to cope with that.
	/// </summary>
	public Func<object?>? ResolveParameter { get; init; }

	/// <summary>
	/// Whether the gesture also fires while a text input has the focus. Default false: then a gesture without Ctrl/Alt, or one that
	/// is a text-editing shortcut (Ctrl+C/V/X/A/Z/Y, Ctrl+Backspace/Delete), is left to the text box.
	/// </summary>
	public bool AllowInTextInput { get; init; }

	/// <summary>
	/// Narrows where the gesture fires: it is given the element that has the keyboard focus and the key is left alone when it answers false. Null = wherever
	/// <see cref="AllowInTextInput"/> and the key policy allow it. Send uses it to stay out of the Find box and the suggestion list.
	/// </summary>
	public Func<object?, bool>? AllowsFocus { get; init; }

	/// <summary>
	/// True for a command that must run once per key press (a destructive one such as Delete): the auto-repeat of a held key is swallowed.
	/// Default false: a held key repeats the command (Find next, for instance).
	/// </summary>
	public bool IgnoreRepeat { get; init; }
}
