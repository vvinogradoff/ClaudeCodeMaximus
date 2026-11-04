using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TessynDesktop.ViewModels;

namespace TessynDesktop.Views;

/// <remarks>Created by Claude</remarks>
public partial class FocusItemCard : UserControl
{
	public FocusItemCard()
	{
		InitializeComponent();
	}

	/// <summary>Clicking the header row of a closed card toggles its body (FR.23.14).</summary>
	private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
	{
		if (DataContext is FocusItemViewModel { IsClosed: true } card)
			card.IsBodyExpanded = !card.IsBodyExpanded;
	}

	/// <summary>An option button puts its text in the card's reply box (FR.23.14).</summary>
	private void OnOptionClick(object? sender, RoutedEventArgs e)
	{
		if (sender is Button { Content: string option } && DataContext is FocusItemViewModel card)
			card.UseOption(option);
	}
}
