using Avalonia.Controls;
using Avalonia.Input;
using ClaudeMaximus.ViewModels;

namespace ClaudeMaximus.Views;

/// <remarks>Created by Claude</remarks>
public partial class RecentSessionsView : UserControl
{
	public RecentSessionsView()
	{
		InitializeComponent();
	}

	private void OnSessionPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (sender is not Border border || border.DataContext is not SessionNodeViewModel session)
			return;

		if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
			return;

		if (DataContext is RecentSessionsViewModel vm)
			vm.SelectedSession = session;
	}
}
