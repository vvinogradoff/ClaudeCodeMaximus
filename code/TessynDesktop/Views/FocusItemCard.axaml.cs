using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TessynDesktop.ViewModels;

namespace TessynDesktop.Views;

/// <remarks>Created by Claude</remarks>
public partial class FocusItemCard : UserControl
{
	public FocusItemCard()
	{
		InitializeComponent();
		// Tunnel: the multi-line reply box handles Enter itself, so Ctrl+Enter is taken before it gets there.
		AddHandler(KeyDownEvent, OnReplyKeyDown, RoutingStrategies.Tunnel);
	}

	/// <summary>Ctrl+Enter in a reply box = "Send answers" (FR.23.15); plain Enter still inserts a newline.</summary>
	private void OnReplyKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Source is not TextBox || !FocusSendShortcut.Matches(e.Key, e.KeyModifiers))
			return;
		e.Handled = true;
		if (this.FindAncestorOfType<FocusView>()?.DataContext is FocusViewModel focus)
			focus.TrySendAnswers();
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
