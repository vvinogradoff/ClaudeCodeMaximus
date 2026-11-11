using Avalonia.Controls;
using Avalonia.Input;
using TessynDesktop.ViewModels;

namespace TessynDesktop.Views;

/// <summary>
/// Collapsible directory groups of session rows, shared by the Recent tab (FR.1.16) and the Tags tab (FR.24.12).
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class SessionGroupListView : UserControl
{
	public SessionGroupListView()
	{
		InitializeComponent();
	}

	private void OnGroupHeaderPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is not Border border || border.DataContext is not RecentSessionGroupViewModel group)
			return;

		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;

		group.IsExpanded = !group.IsExpanded;
	}

	private void OnSessionPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is not Border border || border.DataContext is not SessionNodeViewModel session)
			return;

		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;

		// Always forwarded, even for a repeat click on the same row (FR.1.16).
		if (DataContext is ISessionGroupListHost host)
			host.SelectSession(session);
	}
}
