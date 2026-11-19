namespace TessynDesktop.WPF;

/// <summary>Texts, limits and timings of the docked preview panels (FR.29.17–FR.29.22).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class PreviewDock
	{
		/// <summary>A change of a panel's layout is saved this long after the last one (FR.29.21).</summary>
		public static readonly TimeSpan LayoutSaveDelay = TimeSpan.FromMilliseconds(500);

		/// <summary>The first-time width of a panel docked right or left, as a fraction of the docking area's width (FR.29.21, decision 6).</summary>
		public const double FirstTimeWidthFraction = 0.4;

		/// <summary>The size of a panel docked top or bottom when nothing is remembered, as a fraction of the docking area's height.</summary>
		public const double FirstTimeHeightFraction = 0.4;

		/// <summary>The size used when the docking area has not been laid out yet (no width to take 40 % of).</summary>
		public const double FallbackPaneSize = 480;

		/// <summary>The smallest pane size a remembered layout is restored with.</summary>
		public const double MinPaneSize = 120;

		/// <summary>The id of the central document: the session pane.</summary>
		public const string SessionDocumentId = "session";

		public const string SessionDocumentTitle = "Session";

		/// <summary>The id of a pinned panel (group id, unique suffix).</summary>
		public const string PinnedContentIdFormat = "{0}#pinned{1}";

		/// <summary>Automation names of a panel's buttons; the group name makes them unique per panel (the default panel has neither button).</summary>
		public const string PinAutomationNameFormat = "Pin panel {0}";

		public const string AutoCloseAutomationNameFormat = "Auto-close panel {0}";

		public const string SourceAutomationNameFormat = "Show source of panel {0}";

		public const string OpenExternalAutomationNameFormat = "Open in default app from panel {0}";

		/// <summary>Automation id of a panel's content, per group id.</summary>
		public const string PanelAutomationIdFormat = "PreviewPanel.{0}";

		public const string PanelOpenedLog = "Preview panel opened: group {GroupId}";

		public const string PanelClosedLog = "Preview panel closed: group {GroupId} (by user {ByUser})";

		public const string PanelPinnedLog = "Preview panel pinned: group {GroupId}";

		public const string LayoutSavedLog = "Preview panel layout saved: group {GroupId} (side {Side}, autoHide {AutoHide}, floating {Floating})";
	}
}
