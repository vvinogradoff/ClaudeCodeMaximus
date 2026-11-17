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
		public const string Hint = "Colours as #RRGGBB (or #AARRGGBB). Light and dark themes have their own defaults, which follow the Avalonia client; a colour you set is used in both.";
		public const string InvalidFormat = "{0}: '{1}' is not a colour (use #RRGGBB or #AARRGGBB).";
		public const string ResetAll = "Reset all colours";
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
