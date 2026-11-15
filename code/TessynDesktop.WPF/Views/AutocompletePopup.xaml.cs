using System.Windows;
using System.Windows.Controls;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The list of suggestions shown in the popup of the prompt input. A click on a row raises <see cref="ItemChosen"/>; the keyboard belongs to the input box.</summary>
/// <remarks>Created by Claude</remarks>
public partial class AutocompletePopup : UserControl
{
	public AutocompletePopup()
	{
		InitializeComponent();
	}

	/// <summary>The user clicked the suggestion at this index.</summary>
	public event EventHandler<int>? ItemChosen;

	private void OnRowClick(object sender, RoutedEventArgs e)
	{
		if (sender is not FrameworkElement { DataContext: AutocompleteItemViewModel item })
			return;

		var index = List.Items.IndexOf(item);
		if (index >= 0)
			ItemChosen?.Invoke(this, index);
	}

	private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (List.SelectedItem != null)
			List.ScrollIntoView(List.SelectedItem);
	}
}
