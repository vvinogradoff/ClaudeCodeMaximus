namespace TessynDesktop.WPF;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Texts, command ids and log formats of the docked shell's layout core (FR.6.11–FR.6.19, FR.6.22).</summary>
	public static class ShellLayout
	{
		public const string SessionsTitle = "Sessions";

		public const string SessionTitle = "Session";

		public const string FocusTitle = "Focus";

		/// <summary>The id the layout log line uses for the central Session view.</summary>
		public const string SessionViewLogName = "session";

		/// <summary>The group of the hamburger menu that holds the Panels entries and Reset layout (FR.6.14).</summary>
		public const string PanelsMenuGroup = "panels";

		/// <summary>Order of the Panels entries in <see cref="Menus.Main"/>: after Settings (900), before Exit, which the title bar adds itself.</summary>
		public const int PanelsMenuOrder = 1000;

		public const string ShowSessionsCommandId = "view.showSessions";

		public const string ShowRecentCommandId = "view.showRecent";

		public const string ShowTagsCommandId = "view.showTags";

		public const string ShowFocusCommandId = "view.showFocus";

		public const string ResetCommandId = "view.resetLayout";

		public const string ResetCommandTitle = "Reset layout…";

		public const string ResetDialogTitle = "Reset layout";

		public const string ResetDialogMessage = "Reset the window layout? Panels you moved, floated or closed go back to their default places.";

		public const string ResetDialogConfirm = "Reset";

		/// <summary>Smallest width of the left pane a remembered or migrated width is brought to.</summary>
		public const double MinLeftPaneWidth = 160;

		/// <summary>The Session view keeps at least this width (DIP) when a saved layout is restored into a smaller window (FR.6.18).</summary>
		public const double MinSessionWidth = 480;

		public const string SaveFailedLog = "The shell layout could not be saved";

		public const string SlideOutFailedLog = "The slide-out of panel {PanelId} did not open";

		public const string LayoutLogFormat = "Shell layout: {State}";

		public const string LayoutRestoredLog = "Shell layout restored from the saved section";

		public const string LayoutDefaultLog = "Shell layout: default layout used ({Reason})";

		public const string LayoutResetLog = "Shell layout reset to the default";

		public const string LayoutFloatingClampedLog = "Shell layout: floating panel {PanelId} was off every monitor and was moved onto one";

		public const string ReasonNothingSaved = "nothing saved";

		public const string ReasonNotPersistent = "test window without a test data root";

		public const string ReasonVersionFormat = "saved version {0}, this build writes version {1}";

		public const string ReasonUnreadableFormat = "the saved layout cannot be read: {0}";

		public const string ReasonNoSessionView = "the saved layout has no session view";

		/// <summary>The XML element names of AvalonDock's layout that hold a panel; used to strip the panels that are not shell panels before saving.</summary>
		public static readonly string[] PanelElementNames = ["LayoutAnchorable", "LayoutDocument"];

		/// <summary>Container elements that are removed when stripping a panel leaves them empty (never the document pane or the root).</summary>
		public static readonly string[] PrunableContainerNames =
		[
			"LayoutAnchorablePane", "LayoutAnchorablePaneGroup", "LayoutPanel", "LayoutAnchorableFloatingWindow", "LayoutDocumentFloatingWindow", "LayoutAnchorGroup",
			"LayoutDocumentPaneGroup",
		];
	}
}
