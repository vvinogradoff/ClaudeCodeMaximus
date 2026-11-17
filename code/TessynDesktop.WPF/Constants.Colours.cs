namespace TessynDesktop.WPF;

/// <summary>Names and texts of the colour palette (the Colours settings tab and the brushes the views use).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class Colours
	{
		/// <summary>Prefix of the application resource keys: <c>Colour.RecentTier1</c> and so on.</summary>
		public const string ResourcePrefix = "Colour.";

		public const string TabTitle = "Colours";
		public const string Hint = "Pick the theme to edit, then click a swatch to choose a colour. Light and Dark keep their colours separately, and every change shows at once. "
			+ "A colour you have not changed follows the theme's own default (the session colours follow the Avalonia client).";
		public const string LightName = "Light";
		public const string DarkName = "Dark";
		public const string ThemeLabel = "Theme to edit";
		public const string ResetAllFormat = "Reset all {0} colours";
		public const string ResetOne = "Reset";
	}

	/// <summary>The Appearance tab's choice of the date format in the session rows.</summary>
	public static class DateFormats
	{
		public const string Absolute = "yyyy-MM-dd HH:mm";
		public const string AbsoluteLabel = "Date and time (yyyy-MM-dd HH:mm), as in the Avalonia client";
		public const string RelativeLabel = "Relative (clock time today, otherwise 'MMM d')";
		public const string TodayFormat = "HH:mm";
		public const string OtherDayFormat = "MMM d";
	}
}
