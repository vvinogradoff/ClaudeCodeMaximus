using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TessynDesktop.Services;

/// <summary>The tray icon of the windowless daemon host (A23 Q6): a tooltip and an Exit entry, the only way to reach the process.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class HostTray : IDisposable
{
	private const string IconResource = "TessynDesktop.Assets.tray.ico";
	private const string ToolTip = "Tessyn daemon (background host)";
	private const string ExitText = "Exit Tessyn daemon";
	private const string ConfirmTitle = "Exit Tessyn daemon";

	private readonly NotifyIcon _icon;
	private readonly ContextMenuStrip _menu;

	/// <summary>Creates the tray icon on the calling (message loop) thread. Exit asks first when sessions are active and terminates them (<paramref name="exit"/>).</summary>
	public HostTray(DaemonHostExit exit)
	{
		// FR.32.14: an elevated host must still get the tray's clicks from the (normal) shell, and its icon back after Explorer restarts.
		if (!new TrayMessageFilter().AllowWhenElevated())
			Serilog.Log.Warning("Could not let the tray messages through to this elevated host; its tray icon may not respond");

		_menu = new ContextMenuStrip();
		_menu.Items.Add(ExitText, null, async (_, _) =>
		{
			try
			{
				await exit.RequestFromTrayAsync();
			}
			catch (Exception ex)
			{
				Serilog.Log.Error(ex, "The tray Exit failed");
			}
		});

		using var stream = typeof(HostTray).Assembly.GetManifestResourceStream(IconResource)
			?? throw new InvalidOperationException($"The tray icon resource {IconResource} is missing");
		_icon = new NotifyIcon
		{
			Icon = new Icon(stream),
			Text = ToolTip,
			ContextMenuStrip = _menu,
			Visible = true,
		};
	}

	/// <summary>A question with Yes and No on top of everything; true for yes. The host has no main window, so it is a free-standing message box.</summary>
	public static Task<bool> ConfirmAsync(string message)
	{
		var answer = MessageBox.Show(message, ConfirmTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
			MessageBoxDefaultButton.Button2, MessageBoxOptions.DefaultDesktopOnly);
		return Task.FromResult(answer == DialogResult.Yes);
	}

	public void Dispose()
	{
		_icon.Visible = false;
		_icon.Dispose();
		_menu.Dispose();
	}
}
