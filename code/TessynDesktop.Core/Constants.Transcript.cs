namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Limits of the session tag area modes (FR.24.16), shared by the clients that implement them.</summary>
	public static class TagArea
	{
		/// <summary>Lines of chips the area shows in full mode; more scroll vertically.</summary>
		public const int MaxFullLines = 10;

		/// <summary>Layout tolerance when telling a chip's line from its position (layout rounding).</summary>
		public const double LineEpsilon = 0.5;

		public const string HiddenHintFormat = "+{0}";
		public const string HiddenHintToolTipFormat = "{0} more - click to show all tags";
	}

	/// <summary>Input sources and input navigation in the transcript (FR.2.6).</summary>
	public static class InputNav
	{
		/// <summary>One arrow-key step with nothing selected, in pixels.</summary>
		public const double LineStepPx = 40;

		/// <summary>PgUp/PgDn move this fraction of the viewport.</summary>
		public const double PageFraction = 0.875;

		/// <summary>Distance in pixels kept between the top of the viewport and a selected input that was scrolled into view.</summary>
		public const double RevealMarginPx = 8;

		/// <summary>A press and release further apart than this is a drag (text selection), not a click that selects an input.</summary>
		public const double ClickDragTolerancePx = 4;

		public const string LabelScheduled = "SCHEDULED";
		public const string LabelSupervisor = "SUPERVISOR";
		public const string LabelUser = "You";
	}
}
