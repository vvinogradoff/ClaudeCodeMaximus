using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;

namespace TessynDesktop.Services;

/// <summary>The tray icon of the windowless <c>--background</c> daemon host (A23 Q6): a tooltip and an Exit entry, the only way to reach the process.</summary>
/// <remarks>Created by Claude</remarks>
public static class BackgroundTray
{
	private const string IconUri = "avares://TessynDesktop/Assets/avalonia-logo.ico";
	private const string ToolTip = "Tessyn daemon (background host)";
	private const string ExitText = "Exit Tessyn daemon";
	private const string ConfirmTitle = "Exit Tessyn daemon";
	private const string YesText = "Yes, exit";
	private const string NoText = "Cancel";

	/// <summary>The tray icon. Exit asks first when sessions are active and terminates them like the window's close does (<paramref name="exit"/>).</summary>
	public static TrayIcons Create(DaemonHostExit exit)
	{
		var exitItem = new NativeMenuItem(ExitText);
		exitItem.Click += (_, _) => Dispatcher.UIThread.Post(async () => await exit.RequestFromTrayAsync());

		var menu = new NativeMenu();
		menu.Add(exitItem);

		var icon = new TrayIcon
		{
			Icon = new WindowIcon(AssetLoader.Open(new Uri(IconUri))),
			ToolTipText = ToolTip,
			Menu = menu,
			IsVisible = true,
		};

		return new TrayIcons { icon };
	}

	/// <summary>A small window with a question and two buttons; true for yes. The host has no main window, so it is a free-standing one.</summary>
	public static Task<bool> ConfirmAsync(string message)
	{
		var result = new TaskCompletionSource<bool>();
		var window = new Window
		{
			Title = ConfirmTitle,
			Width = 460,
			SizeToContent = SizeToContent.Height,
			WindowStartupLocation = WindowStartupLocation.CenterScreen,
			CanResize = false,
			Topmost = true,
		};

		var yes = new Button { Content = YesText, Margin = new Thickness(0, 0, 8, 0) };
		var no = new Button { Content = NoText, IsDefault = true };
		yes.Click += (_, _) => { result.TrySetResult(true); window.Close(); };
		no.Click += (_, _) => { result.TrySetResult(false); window.Close(); };
		window.Closed += (_, _) => result.TrySetResult(false);
		window.Content = new StackPanel
		{
			Margin = new Thickness(20),
			Spacing = 16,
			Children =
			{
				new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
				new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { yes, no } },
			},
		};
		window.Show();
		return result.Task;
	}
}
