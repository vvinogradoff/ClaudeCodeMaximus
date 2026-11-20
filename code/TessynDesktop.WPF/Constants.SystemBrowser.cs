namespace TessynDesktop.WPF;

/// <summary>Texts of the forced system-browser open (FR.26.9): menu entries, tooltip line, page message flag.</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class SystemBrowser
	{
		public const string OpenInSystemBrowser = "Open in system browser";
		public const string OpenInSystemBrowserHint = "Ctrl+Shift+click";

		/// <summary>The tooltip line of Focus card links, Focus url references and chips.</summary>
		public const string CtrlClickLine = "Ctrl+click: open in the system browser";

		/// <summary>The flag of the page's <c>link</c> message that asks for the system browser.</summary>
		public const string PageMessageFlag = "sys";
	}
}
