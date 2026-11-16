using System.Windows.Input;

namespace TessynDesktop.WPF.Views;

/// <summary>The key chord that sends every filled Focus reply box (FR.23.15): Ctrl+Enter, with no Shift or Alt. Plain Enter stays a newline.</summary>
/// <remarks>Created by Claude</remarks>
public static class FocusSendShortcut
{
	public static bool Matches(Key key, ModifierKeys modifiers) => key == Key.Enter && modifiers == ModifierKeys.Control;
}
