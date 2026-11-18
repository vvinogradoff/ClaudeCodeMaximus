namespace TessynDesktop.WPF;

/// <summary>Texts, timings and sizes of the embedded browser of web preview windows (FR.27).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class Browser
	{
		/// <summary>Default size of a popup window when the page gives none (DIPs).</summary>
		public const double PopupDefaultWidth = 600;
		public const double PopupDefaultHeight = 700;

		/// <summary>A popup is never smaller than this (DIPs).</summary>
		public const double PopupMinSize = 200;

		/// <summary>Off-screen position of windows in test mode.</summary>
		public const double TestWindowOffset = -32000;

		/// <summary>The never-shown window that clears the browser data when no browser window is open (DIPs).</summary>
		public const double CleanerWindowSize = 100;

		public const string BlankUri = "about:blank";
		public const string NotAllowedMessage = "Only http and https addresses can be opened here.";
		public const string NavigationFailedFormat = "The page could not be loaded ({0}).";
		public const string BrowserLostMessage = "The browser stopped unexpectedly. Reload the page to continue.";
		public const string UntitledPopup = "Browser";

		public const string BackName = "Browser back";
		public const string ForwardName = "Browser forward";
		public const string ReloadName = "Reload page";
		public const string StopName = "Stop loading";
		public const string AddressName = "Page address";
		public const string OpenInSystemBrowserName = "Open in system browser";
		public const string DismissName = "Dismiss error";
	}
}
