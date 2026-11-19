using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AvalonDock.Layout;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Chooses the content of a panel's tab header (FR.6.16): the Focus panel shows its title and the badge (<see cref="FocusTabHeader"/>, data context = the pane's
/// <c>FocusViewModel</c>, i.e. the data context of the panel's content), every other panel its plain title.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellPanelHeaderTemplateSelector : DataTemplateSelector
{
	private readonly DataTemplate _focus = CreateFocusTemplate();
	private readonly DataTemplate _plain = CreatePlainTemplate();

	public override DataTemplate SelectTemplate(object? item, DependencyObject container) =>
		item is LayoutAnchorable { ContentId: Constants.DockedShell.FocusPanelId } ? _focus : _plain;

	private static DataTemplate CreateFocusTemplate()
	{
		var header = new FrameworkElementFactory(typeof(FocusTabHeader));
		header.SetBinding(FrameworkElement.DataContextProperty, new Binding($"{nameof(LayoutContent.Content)}.{nameof(FrameworkElement.DataContext)}"));
		var template = new DataTemplate { VisualTree = header };
		template.Seal();
		return template;
	}

	private static DataTemplate CreatePlainTemplate()
	{
		var text = new FrameworkElementFactory(typeof(TextBlock));
		text.SetBinding(TextBlock.TextProperty, new Binding(nameof(LayoutContent.Title)));
		text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
		var template = new DataTemplate { VisualTree = text };
		template.Seal();
		return template;
	}
}
