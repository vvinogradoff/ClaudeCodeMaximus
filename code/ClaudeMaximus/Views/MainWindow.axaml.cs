using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ClaudeMaximus.ViewModels;
using ReactiveUI;

namespace ClaudeMaximus.Views;

/// <remarks>Created by Claude</remarks>
public partial class MainWindow : Window
{
	private bool _closeConfirmed;

	public MainWindow()
	{
		InitializeComponent();
	}

	protected override async void OnClosing(WindowClosingEventArgs e)
	{
		base.OnClosing(e);

		if (_closeConfirmed) return;

		if (DataContext is not MainWindowViewModel vm) return;

		var count = vm.ActiveSessionCount;
		if (count == 0) return;

		e.Cancel = true;

		var confirmed = await ShowCloseConfirmAsync(count);
		if (!confirmed) return;

		vm.TerminateAllSessions();
		_closeConfirmed = true;
		Close();
	}

	private async Task<bool> ShowCloseConfirmAsync(int count)
	{
		var tcs    = new TaskCompletionSource<bool>();
		var dialog = new Window
		{
			Title  = "Close ClaudeMaximus",
			Width  = 440,
			Height = 170,
			WindowStartupLocation = WindowStartupLocation.CenterOwner,
			CanResize = false,
			ShowInTaskbar = false,
		};

		var noun = count == 1 ? "session is" : "sessions are";
		var label = new TextBlock
		{
			Text        = $"There are {count} Claude Code {noun} currently active.\nAre you sure you want to terminate them and close the application?",
			TextWrapping = TextWrapping.Wrap,
			Margin      = new Thickness(0, 0, 0, 16),
		};

		var cancelBtn = new Button { Content = "Cancel",     HorizontalAlignment = HorizontalAlignment.Left };
		var closeBtn  = new Button { Content = "Yes, close", HorizontalAlignment = HorizontalAlignment.Right };

		cancelBtn.Click += (_, _) => { tcs.TrySetResult(false); dialog.Close(); };
		closeBtn.Click  += (_, _) => { tcs.TrySetResult(true);  dialog.Close(); };

		var buttons = new Grid();
		buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
		buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
		buttons.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
		Grid.SetColumn(cancelBtn, 0);
		Grid.SetColumn(closeBtn,  2);
		buttons.Children.Add(cancelBtn);
		buttons.Children.Add(closeBtn);

		var panel = new StackPanel { Margin = new Thickness(20), Spacing = 0 };
		panel.Children.Add(label);
		panel.Children.Add(buttons);

		dialog.Content = panel;
		dialog.Closed += (_, _) => tcs.TrySetResult(false);

		await dialog.ShowDialog(this);
		return tcs.Task.Result;
	}

	protected override void OnDataContextChanged(System.EventArgs e)
	{
		base.OnDataContextChanged(e);

		if (DataContext is MainWindowViewModel vm)
		{
			// Wire ActiveSession changes to SessionView's DataContext
			vm.WhenAnyValue(x => x.ActiveSession)
				.Subscribe(session => SessionViewPanel.DataContext = session);
		}
	}

	protected override void OnClosed(System.EventArgs e)
	{
		if (DataContext is MainWindowViewModel vm)
		{
			var settings = App.Services.GetService(typeof(Services.IAppSettingsService))
				as Services.IAppSettingsService;

			if (settings != null)
			{
				settings.Settings.Window.Width    = Width;
				settings.Settings.Window.Height   = Height;
				settings.Settings.Window.Left     = Position.X;
				settings.Settings.Window.Top      = Position.Y;
				settings.Settings.Window.SplitterPosition = vm.SplitterPosition;
				settings.Save();
			}
		}

		base.OnClosed(e);
	}
}
