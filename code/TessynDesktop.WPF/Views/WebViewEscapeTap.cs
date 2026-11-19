using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Reports the Esc key pressed while the keyboard focus is inside a WebView2 page (FR.6.13, FR.29 decision 4). A key pressed in the browser does not reach WPF's key events, so the slide-out
/// of an auto-hidden web panel could not be closed with Esc then. The browser's own <c>CoreWebView2Controller.AcceleratorKeyPressed</c> sees it; the WPF control keeps its controller private,
/// so it is read by the exact member names, guarded: where it is missing (another WebView2 version) one warning is logged and Esc simply does not close the slide-out over a page.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class WebViewEscapeTap
{
	/// <summary>The WPF control keeps its implementation in a private field of this name; that object has a public <c>CoreWebView2Controller</c> property.</summary>
	public const string BaseField = "m_webview2Base";

	public const string ControllerProperty = "CoreWebView2Controller";

	private const uint EscapeKey = 0x1B;

	/// <summary>Starts reporting; true when the browser's controller was found. <paramref name="onEscape"/> runs on the UI thread.</summary>
	public bool Hook(WebView2 control, Action onEscape, ILogger? logger)
	{
		try
		{
			var implementation = typeof(WebView2).GetField(BaseField, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(control);
			var controller = implementation?.GetType().GetProperty(ControllerProperty, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(implementation) as CoreWebView2Controller;
			if (controller == null)
			{
				logger?.LogWarning("Browser: the WebView2 controller was not found ({Field}.{Property}); Esc in a page does not close the slide-out", BaseField, ControllerProperty);
				return false;
			}

			controller.AcceleratorKeyPressed += (_, e) =>
			{
				if (e.KeyEventKind == CoreWebView2KeyEventKind.KeyDown && e.VirtualKey == EscapeKey)
					onEscape();
			};
			return true;
		}
		catch (Exception ex)
		{
			logger?.LogWarning(ex, "Browser: hooking Esc in the WebView2 failed; Esc in a page does not close the slide-out");
			return false;
		}
	}
}
