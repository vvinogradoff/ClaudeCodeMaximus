using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace TessynDesktop.WPF.Services;

/// <summary>Decides whether a window-level gesture may take a key press away from a focused text input.</summary>
/// <remarks>Created by Claude</remarks>
public static class ShellKeyPolicy
{
	/// <summary>True when the element is somewhere the user types text.</summary>
	public static bool IsTextInput(object? focused) => focused is TextBoxBase or PasswordBox or ComboBox { IsEditable: true };

	/// <summary>True when a text input with the focus keeps this key press from the command.</summary>
	public static bool Blocks(ShellCommand command, Key key, ModifierKeys modifiers, bool textInputFocused)
	{
		if (!textInputFocused || command.AllowInTextInput)
			return false;

		if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0)
			return true;

		return (modifiers & ModifierKeys.Control) != 0 && key is Key.C or Key.V or Key.X or Key.A or Key.Z or Key.Y or Key.Back or Key.Delete or Key.Insert;
	}
}
