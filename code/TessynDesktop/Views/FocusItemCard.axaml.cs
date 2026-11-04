using Avalonia.Controls;
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

	/// <summary>An option button puts its text in the card's reply box (FR.23.14).</summary>
	private void OnOptionClick(object? sender, RoutedEventArgs e)
	{
		if (sender is Button { Content: string option } && DataContext is FocusItemViewModel card)
			card.UseOption(option);
	}
}
