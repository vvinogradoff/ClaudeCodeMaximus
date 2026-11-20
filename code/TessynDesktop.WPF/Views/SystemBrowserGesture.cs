using System.Windows.Input;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The forced system-browser gesture of the Focus panel and the session chips (FR.26.9, decision A38): Ctrl+left-click with no Shift and no Alt.
/// (The transcript uses Ctrl+Shift+click, where Ctrl+click is the normal routed open; that gesture lives in the page script.)
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SystemBrowserGesture
{
	/// <summary>True when <paramref name="modifiers"/> are exactly Ctrl.</summary>
	public bool Matches(ModifierKeys modifiers) => (modifiers & ModifierKeys.Control) != 0 && (modifiers & (ModifierKeys.Shift | ModifierKeys.Alt)) == 0;

	/// <summary>Runs <paramref name="command"/> when the gesture matches and the command can run; true when it did (the press is then taken).</summary>
	public bool TryRun(System.Windows.Input.ICommand command, ModifierKeys modifiers)
	{
		if (!Matches(modifiers) || !command.CanExecute(null))
			return false;

		command.Execute(null);
		return true;
	}
}
