using System.Windows;
using System.Windows.Controls;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>Picks the card of an open Focus item: the ordinary card, or the elevated-run card (FR.23.32) for an item that carries the <c>elevatedRun</c> action.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusCardTemplateSelector : DataTemplateSelector
{
	public DataTemplate? Standard { get; set; }

	public DataTemplate? Elevated { get; set; }

	public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
		item is FocusItemViewModel { IsElevatedRun: true } ? Elevated : Standard;
}
