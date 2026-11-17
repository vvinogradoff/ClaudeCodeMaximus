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

	/// <summary>The Appearance tab's choice of the date format in the session rows: exactly two forms.</summary>
	public static class DateFormats
	{
		public const string Absolute = "yyyy-MM-dd";
		public const string AbsoluteLabel = "Date (yyyy-MM-dd)";
		public const string RelativeLabel = "Relative (for example 107w)";
		public const string NowText = "now";
		public const string MinutesUnit = "m";
		public const string HoursUnit = "h";
		public const string DaysUnit = "d";
		public const string WeeksUnit = "w";
		public const int MaxDaysAsDays = 14;
		public const int DaysPerWeek = 7;
	}
}