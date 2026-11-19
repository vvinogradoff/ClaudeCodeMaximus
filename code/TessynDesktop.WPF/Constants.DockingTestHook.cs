namespace TessynDesktop.WPF;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>The programmatic docking test hook (FR.6.22, FR.29.26): the pipe, the operations, the texts. Active only under <c>--test-window</c>.</summary>
	public static class DockingTestHook
	{
		/// <summary>The pipe is named <c>tessyn-wpf-test-&lt;pid&gt;</c>.</summary>
		public const string PipeNamePrefix = "tessyn-wpf-test-";

		/// <summary>How often the UI Automation names of the tabs are tried while the library has not filled all of them yet (FR.6.22).</summary>
		public const int NameAttempts = 10;

		/// <summary>Joins the titles of the panels of a floating frame that holds several, in its UI Automation name.</summary>
		public const string TitleSeparator = ", ";

		public const string OpState = "state";
		public const string OpDock = "dock";
		public const string OpTab = "tab";
		public const string OpFloat = "float";
		public const string OpAutoHide = "autohide";
		public const string OpSlideOut = "slideout";
		public const string OpClose = "close";
		public const string OpShow = "show";
		public const string OpBringForward = "bringforward";
		public const string OpReset = "reset";

		public const string ArgOn = "on";
		public const string ArgOff = "off";

		public const string KindShell = "shell";
		public const string KindPreview = "preview";
		public const string KindSession = "session";

		public const string SideCenter = "center";
		public const string SideFloating = "floating";
		public const string SideClosed = "closed";

		public const string VerbDocked = "docked";
		public const string VerbStacked = "stacked";
		public const string VerbFloated = "floated";
		public const string VerbClosed = "closed";

		public const string UnreadableRequestError = "The request is not a JSON object: {0}";
		public const string UnknownOperationError = "Unknown operation '{0}'.";
		public const string MissingArgumentError = "Operation '{0}' needs '{1}'.";
		public const string UnknownPanelError = "Unknown panel '{0}'.";
		public const string UnknownSideError = "Unknown side '{0}' (left, right, top or bottom).";
		public const string UnknownModeError = "Unknown mode '{0}' (on or off).";
		public const string SessionRefusedError = "The Session view cannot be {0}.";
		public const string NotInLayoutError = "Panel '{0}' is not open.";
		public const string NotDockedError = "Panel '{0}' is floating or has no pane to be auto-hidden.";
		public const string NotAutoHiddenError = "Panel '{0}' is not auto-hidden.";
		public const string SlideOutFailedError = "Panel '{0}' did not slide out.";
		public const string TabTargetError = "Panel '{1}' is not docked, so '{0}' cannot be stacked on it.";
		public const string TabIntoSessionError = "Panel '{0}' cannot become a tab of the session area.";
		public const string OperationFailedError = "Operation '{0}' failed: {1}";

		public const string StartedLog = "Docking test hook listening on pipe {Pipe}";
		public const string NotStartedLog = "Docking test hook not started: {Reason}";
		public const string RequestFailedLog = "Docking test hook request failed";
		public const string ConnectionFailedLog = "Docking test hook connection failed";
		public const string StoppedLog = "Docking test hook stopped";

		public const string FloatingPlacedLog = "Test window: floating panel window made non-activating, off-screen and out of the taskbar";
	}
}
