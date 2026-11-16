using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Where Send's key gesture may fire. It is a window-level command now (so an imported or edited key works), but Ctrl+Enter typed into any other text box
/// (the Find box) or into the suggestion list is not a request to send the prompt.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class SendFocusPolicy
{
	/// <summary>True when the focused element is the prompt box, or is not a text input and not part of the suggestions.</summary>
	public static bool Allows(object? focused)
	{
		if (focused is not DependencyObject element)
			return true;

		if (element is TextBox { Name: Constants.Appearance.PromptBoxName })
			return true;

		return !ShellKeyPolicy.IsTextInput(element) && !IsInSuggestions(element);
	}

	private static bool IsInSuggestions(DependencyObject element)
	{
		for (var current = element; current != null; current = Parent(current))
		{
			if (current is AutocompletePopup)
				return true;
		}

		return false;
	}

	private static DependencyObject? Parent(DependencyObject element) =>
		element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) ?? LogicalTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}
