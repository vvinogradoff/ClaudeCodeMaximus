using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Attached behaviour: <c>v:CommandMenu.Menu="tree.node"</c> makes an element's context menu be built, each time it opens, from the
/// entries registered for that menu id in <see cref="IShellCommands"/>. The command parameter is the DataContext of the item under the
/// mouse (a list item), else the element's own DataContext. With no entries no menu opens.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class CommandMenu
{
	public static readonly DependencyProperty MenuProperty = DependencyProperty.RegisterAttached("Menu", typeof(string), typeof(CommandMenu), new PropertyMetadata(null, OnMenuChanged));

	public static string? GetMenu(DependencyObject element) => (string?)element.GetValue(MenuProperty);

	public static void SetMenu(DependencyObject element, string? value) => element.SetValue(MenuProperty, value);

	/// <summary>Builds the menu for <paramref name="menuId"/>; null when it has no entries.</summary>
	public static ContextMenu? Build(IShellCommands commands, string menuId, object? parameter)
	{
		var entries = commands.BuildMenu(menuId, parameter);
		if (entries.Count == 0)
			return null;

		var menu = new ContextMenu();
		foreach (var entry in entries)
		{
			if (entry.BeginsGroup)
				menu.Items.Add(new Separator());

			var item = new MenuItem { Header = entry.Title, InputGestureText = entry.Gesture, IsEnabled = entry.IsEnabled };
			var commandId = entry.CommandId;
			item.Click += async (_, _) => await commands.ExecuteAsync(commandId, parameter);
			menu.Items.Add(item);
		}

		return menu;
	}

	private static void OnMenuChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
	{
		if (element is not FrameworkElement target)
			return;

		target.ContextMenuOpening -= OnContextMenuOpening;
		if (e.NewValue != null)
			target.ContextMenuOpening += OnContextMenuOpening;
	}

	private static void OnContextMenuOpening(object sender, ContextMenuEventArgs e)
	{
		var target = (FrameworkElement)sender;
		var menuId = GetMenu(target);
		var commands = new ShellServiceProvider().Get<IShellCommands>();
		if (menuId == null || commands == null)
		{
			e.Handled = true;
			return;
		}

		var parameter = FindParameter(target, e.OriginalSource as DependencyObject);
		target.ContextMenu = Build(commands, menuId, parameter);
		e.Handled = target.ContextMenu == null;
	}

	private static object? FindParameter(FrameworkElement target, DependencyObject? source)
	{
		var node = source;
		while (node != null && !ReferenceEquals(node, target))
		{
			if (node is ListBoxItem item)
				return item.DataContext;

			node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
		}

		return target.DataContext;
	}
}
