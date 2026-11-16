using System.Windows.Controls;
using System.Windows.Input;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The Focus view: open items of the selected session as cards (FR.23). Pure view; everything is bound to <c>FocusViewModel</c>.</summary>
/// <remarks>Created by Claude</remarks>
public partial class FocusPanel : UserControl
{
	public FocusPanel()
	{
		InitializeComponent();
		PreviewKeyDown += OnPreviewKeyDown;
	}

	/// <summary>Ctrl+Enter in a reply box = "Send answers" (FR.23.15), through the same command as the button and under its enabling rule. Plain Enter still inserts a newline.</summary>
	private void OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (HandleReplyKey(e.OriginalSource, e.Key, Keyboard.Modifiers))
			e.Handled = true;
	}

	/// <summary>True when the key was Ctrl+Enter in a text box (handled, whether or not anything was sent); the send itself obeys the command's CanExecute.</summary>
	public bool HandleReplyKey(object? source, Key key, ModifierKeys modifiers)
	{
		if (source is not TextBox || !FocusSendShortcut.Matches(key, modifiers))
			return false;
		if (DataContext is FocusViewModel { SendAnswersCommand: var command } && command.CanExecute(null))
			command.Execute(null);
		return true;
	}
}
