using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// A popup of the embedded browser (<c>target=_blank</c>, <c>window.open</c>, sign-in popups, FR.27.3): a plain window with one WebView2 in the
/// browser's environment (the same profile, so <c>window.opener</c> flows work) and the page title as its title. Closes when the page asks to. It takes the focus only when <c>activate</c> is set (a user-initiated request).
/// In test mode it is off-screen, never activated and not in the taskbar.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class BrowserPopupWindow : Window
{
	public BrowserPopupWindow(Window? owner, double width, double height, bool activate, bool testMode)
	{
		InitializeComponent();
		Width = Math.Max(Constants.Browser.PopupMinSize, Math.Min(width, SystemParameters.VirtualScreenWidth));
		Height = Math.Max(Constants.Browser.PopupMinSize, Math.Min(height, SystemParameters.VirtualScreenHeight));
		if (owner != null)
			Owner = owner;

		// Only a popup the user asked for (a click) may take the focus; a script or timer popup never does.
		ShowActivated = activate && !testMode;
		if (!testMode)
		{
			WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
			return;
		}

		ShowInTaskbar = false;
		Left = Constants.Browser.TestWindowOffset;
		Top = Constants.Browser.TestWindowOffset;
		SourceInitialized += (_, _) => TestWindowFocusGuard.Apply(new WindowInteropHelper(this).Handle);
	}

	/// <summary>Creates the page in <paramref name="environment"/> (persistent mode, as the opener) and returns its core for <c>NewWindow</c>.</summary>
	public async Task<CoreWebView2> InitializeAsync(CoreWebView2Environment environment)
	{
		var options = environment.CreateCoreWebView2ControllerOptions();
		options.IsInPrivateModeEnabled = false;
		await Page.EnsureCoreWebView2Async(environment, options);
		var core = Page.CoreWebView2;
		core.DocumentTitleChanged += (_, _) => Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? Constants.Browser.UntitledPopup : core.DocumentTitle;
		core.WindowCloseRequested += (_, _) => Close();
		return core;
	}

	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
		Page.Dispose();
	}
}
