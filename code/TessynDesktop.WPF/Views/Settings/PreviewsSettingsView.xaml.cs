using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TessynDesktop.WPF.ViewModels.Settings;

namespace TessynDesktop.WPF.Views.Settings;

/// <summary>The Previews tab (DataContext: <c>PreviewsSectionViewModel</c>).</summary>
/// <remarks>Created by Claude</remarks>
public partial class PreviewsSettingsView : UserControl
{
	private const string NameBoxName = "GroupNameBox";

	public PreviewsSettingsView()
	{
		InitializeComponent();
		DataContextChanged += (_, e) =>
		{
			if (e.OldValue is INotifyPropertyChanged oldContext)
				oldContext.PropertyChanged -= OnPropertyChanged;
			if (e.NewValue is INotifyPropertyChanged newContext)
				newContext.PropertyChanged += OnPropertyChanged;
		};
	}

	/// <summary>A group was added: its name box takes the focus.</summary>
	private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName != nameof(PreviewsSectionViewModel.NewGroup) || sender is not PreviewsSectionViewModel { NewGroup: { } row })
			return;

		Dispatcher.BeginInvoke(() =>
		{
			if (Rows.ItemContainerGenerator.ContainerFromItem(row) is not ListBoxItem item)
				return;

			item.BringIntoView();
			if (FindNamed(item, NameBoxName) is TextBox box)
				box.Focus();
		}, DispatcherPriority.Loaded);
	}

	private static FrameworkElement? FindNamed(DependencyObject parent, string name)
	{
		for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			var child = VisualTreeHelper.GetChild(parent, i);
			if (child is FrameworkElement element && element.Name == name)
				return element;

			var found = FindNamed(child, name);
			if (found != null)
				return found;
		}

		return null;
	}
}
