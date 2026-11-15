namespace TessynDesktop.WPF;

/// <summary>Names and limits of the session tag area (FR.24: the "mini" and "full" modes of the tag cloud under the session header).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class TagArea
	{
		/// <summary>Lines of chips the area shows in full mode; more scroll vertically.</summary>
		public const int MaxFullLines = 10;

		/// <summary>Layout tolerance when telling a chip's line from its position (layout rounding).</summary>
		public const double LineEpsilon = 0.5;

		public const string AutomationName = "Session tags";
		public const string AutomationId = "SessionTagArea";
		public const string HiddenHintFormat = "+{0}";
		public const string HiddenHintToolTipFormat = "{0} more - click to show all tags";
	}
}
