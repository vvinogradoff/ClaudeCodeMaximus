using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;

namespace TessynDesktop.Services;

/// <summary>The tray icon of the windowless <c>--background</c> daemon host (A23 Q6): a tooltip and an Exit entry, the only way to reach the process.</summary>
/// <remarks>Created by Claude</remarks>
public static class BackgroundTray
{
	private const string IconUri = "avares://TessynDesktop/Assets/avalonia-logo.ico";
	private const string ToolTip = "Tessyn daemon (background host)";
	private const string ExitText = "Exit Tessyn daemon";

	public static TrayIcons Create(IClassicDesktopStyleApplicationLifetime desktop)
	{
		var exit = new NativeMenuItem(ExitText);
		exit.Click += (_, _) => desktop.Shutdown();

		var menu = new NativeMenu();
		menu.Add(exit);

		var icon = new TrayIcon
		{
			Icon = new WindowIcon(AssetLoader.Open(new Uri(IconUri))),
			ToolTipText = ToolTip,
			Menu = menu,
			IsVisible = true,
		};

		return new TrayIcons { icon };
	}
}
