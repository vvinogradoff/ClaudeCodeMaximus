namespace TessynDesktop.WPF;

/// <summary>Texts, ids and limits of the theme, fonts, title bar, status bar and screensaver (W10a).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class Appearance
	{
		/// <summary>Ctrl+wheel keeps a font size within these points (as the Avalonia client does).</summary>
		public const double ZoomMin = 8;
		public const double ZoomMax = 32;

		public const string ToggleThemeCommandId = "view.toggleTheme";
		public const string ToggleThemeCommandTitle = "Toggle light/dark theme";
		public const string ToggleTreeCommandId = "view.toggleTree";
		public const string ToggleTreeCommandTitle = "Show or hide the session tree";
		public const string SendCommandId = "session.send";
		public const string SendCommandTitle = "Send the prompt";
		public const string SendCommandGesture = "Ctrl+Enter";

		/// <summary>The theme dictionaries swapped into the application resources.</summary>
		public const string LightThemeUri = "pack://application:,,,/TessynDesktop.WPF;component/Styles/Theme.Light.xaml";
		public const string DarkThemeUri = "pack://application:,,,/TessynDesktop.WPF;component/Styles/Theme.Dark.xaml";
		public const string ThemeDictionaryMarker = "/Styles/Theme.";

		/// <summary>Application resource key of the prompt box font size (DIPs).</summary>
		public const string InputFontSizeKey = "InputFontSize";

	}

	public static class TitleBar
	{
		public const string TreeToggleAutomationName = "Toggle session tree";
		public const string MenuAutomationName = "Main menu";
		public const string ThemeToggleAutomationName = "Toggle theme";
		public const string MinimizeAutomationName = "Minimize";
		public const string MaximizeAutomationName = "Maximize";
		public const string RestoreAutomationName = "Restore";
		public const string CloseAutomationName = "Close";
		public const string BuildOutputWarning = "Running from the project build output: it will not be updated on restart";
		public const string BuildNumberFormat = "yyyyMM.dd.HHmm";
		public const string ExitMenuText = "Exit";
		public const string SettingsMenuText = "Settings";

		/// <summary>Height of the caption area in DIPs.</summary>
		public const double Height = 32;
		public const double ResizeBorder = 6;
	}

	public static class StatusBar
	{
		public const int ItemOrder = 5;
		public const string FiveHourFormat = "5h: {0:0}%";
		public const string SevenDayFormat = "7d: {0:0}%";
		public const string FiveHourResetFormat = "5h: {0:0}%  resets {1:HH:mm}";
		public const string SevenDayResetFormat = "7d: {0:0}%  resets {1:ddd}";
		public const string LocalModelSuffix = "local";
		public const string UsageUnauthorized = "Usage: sign in again";
		public const string UsageUnavailable = "Usage unavailable";
		public const string FiveHourAutomationName = "5-hour usage";
		public const string SevenDayAutomationName = "7-day usage";
		public const string ModelAutomationName = "Model";
		public const string UsageNoteAutomationName = "Usage status";
		public const string FiveHourId = "5h";
		public const string SevenDayId = "7d";
		public const string DefaultProfileId = "default";
	}

	public static class Screensaver
	{
		public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"];

		/// <summary>The slideshow decodes an image no wider than this many pixels, so one 8K photo cannot take hundreds of megabytes.</summary>
		public const int MaxDecodePixels = 3840;

		public const double TickSeconds = 1;
		public const string AutomationName = "Screensaver";
	}
}
