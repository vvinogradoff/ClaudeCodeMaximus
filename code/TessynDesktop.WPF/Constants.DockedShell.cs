namespace TessynDesktop.WPF;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>The docked shell (FR.6.11–FR.6.22): stable panel ids, default sizes, the layout section version.</summary>
	public static class DockedShell
	{
		public const string SessionsPanelId = "sessions";
		public const string RecentPanelId = "recent";
		public const string TagsPanelId = "tags";
		public const string SessionPanelId = "session";
		public const string FocusPanelId = "focus";

		/// <summary>The log line written when Focus gives way to the Session view because the shown session has no open items (FR.6.25.4); the argument is the node id.</summary>
		public const string FocusAutoSwitchLog = "Session {NodeId}: no open Focus items, back to the Session view";

		/// <summary>Default width of the left pane in device-independent pixels (FR.6.12).</summary>
		public const double DefaultLeftPaneWidth = 320;

		/// <summary>Default share of the docking area's width for the pane that holds Focus (FR.6.12).</summary>
		public const double DefaultFocusPaneShare = 0.35;

		/// <summary>Version of the saved layout; a saved layout with another version is ignored (FR.6.18).</summary>
		public const int LayoutVersion = 1;

		/// <summary>The layout is saved this long after the last change (FR.6.18).</summary>
		public static readonly TimeSpan LayoutSaveDelay = TimeSpan.FromMilliseconds(500);
	}
}
