using Avalonia.Controls;
using Avalonia.Input;
using TessynDesktop.ViewModels;

namespace TessynDesktop.Views;

/// <summary>The Tags tab (FR.24.11, FR.24.12): left click toggles a chip, the context menu opens its URL.</summary>
/// <remarks>Created by Claude</remarks>
public partial class TagSearchView : UserControl
{
	public TagSearchView()
	{
		InitializeComponent();
	}

	private void OnTagChipPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is not Border border || border.DataContext is not TagSearchChipViewModel chip)
			return;

		// Right click is left to the context menu (FR.24.12).
		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;

		chip.Toggle();
	}

	private void OnOpenInBrowserClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
	{
		if (sender is not MenuItem item)
			return;

		var chip = item.DataContext as TagSearchChipViewModel
			?? (item.Parent as ContextMenu)?.PlacementTarget?.DataContext as TagSearchChipViewModel;
		chip?.Open();
	}
}
