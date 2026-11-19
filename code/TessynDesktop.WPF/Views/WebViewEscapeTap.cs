using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// Reports the Esc key pressed while the keyboard focus is inside a WebView2 page (FR.6.13, FR.29 decision 4). A key pressed in the browser does not reach WPF's key events, so the slide-out
/// of an auto-hidden web panel could not be closed with Esc then. The browser's own <c>CoreWebView2Controller.AcceleratorKeyPressed</c> sees it; the WPF control keeps its controller private,
/// so it is read by the exact member names (<see cref="Constants.WebViewEscapeTap"/>), guarded: where it is missing (another WebView2 version) one warning is logged for the whole process (one
/// instance serves every surface) and Esc simply does not close the slide-out over a page.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class WebViewEscapeTap
{
	private bool _warned;

	/// <summary>Starts reporting; true when the browser's controller was found. <paramref name="onEscape"/> runs on the UI thread.</summary>
	public bool Hook(WebView2 control, Action onEscape, ILogger? logger)
	{
		try
		{
			var implementation = typeof(WebView2).GetField(Constants.WebViewEscapeTap.BaseField, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(control);
			var controller = implementation?.GetType().GetProperty(Constants.WebViewEscapeTap.ControllerProperty, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(implementation) as CoreWebView2Controller;
			if (controller == null)
			{
				Warn(logger, null, Constants.WebViewEscapeTap.ControllerMissingLog, Constants.WebViewEscapeTap.BaseField, Constants.WebViewEscapeTap.ControllerProperty);
				return false;
			}

			controller.AcceleratorKeyPressed += (_, e) =>
			{
				if (e.KeyEventKind == CoreWebView2KeyEventKind.KeyDown && e.VirtualKey == Constants.WebViewEscapeTap.EscapeKey)
					onEscape();
			};
			return true;
		}
		catch (Exception ex)
		{
			Warn(logger, ex, Constants.WebViewEscapeTap.HookFailedLog);
			return false;
		}
	}

	private void Warn(ILogger? logger, Exception? exception, string message, params object[] arguments)
	{
		if (_warned)
			return;

		_warned = true;
		logger?.LogWarning(exception, message, arguments);
	}
}
