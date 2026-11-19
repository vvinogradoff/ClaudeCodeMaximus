using System.Windows;
using System.Windows.Input;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Routes the key gestures a floating panel's window leaves unhandled to the shell command bindings, exactly as <c>MainWindow.OnPreviewKeyDown</c> does (FR.6.20): a floating
/// panel is a window of its own, so the main window never sees its keys.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DockingKeyboardRouter
{
	private IShellCommands? _commands;

	/// <summary>The command registry the gestures go to; floating windows created before it is set pass their keys on from then on.</summary>
	public void UseCommands(IShellCommands commands) => _commands = commands;

	/// <summary>Hooks a floating window (or any element of one).</summary>
	public void Attach(UIElement window) => window.PreviewKeyDown += OnPreviewKeyDown;

	private void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Handled || _commands == null)
			return;

		var key = e.Key == Key.System ? e.SystemKey : e.Key;
		if (_commands.TryExecuteGesture(key, Keyboard.Modifiers, ShellKeyPolicy.IsTextInput(Keyboard.FocusedElement), e.IsRepeat, Keyboard.FocusedElement))
			e.Handled = true;
	}
}
