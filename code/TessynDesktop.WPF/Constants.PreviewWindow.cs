namespace TessynDesktop.WPF;

/// <summary>Texts, automation names, sizes and timings of the preview windows' view layer and of the title-bar Monitor toggle (FR.29.5–FR.29.8, FR.6.10).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class PreviewWindow
	{
		/// <summary>The title is <c>item - group</c> (FR.29.5).</summary>
		public const string TitleFormat = "{0} - {1}";

		/// <summary>The title while a window has no item yet.</summary>
		public const string UntitledItem = "Preview";

		public const string PinAutomationName = "Pin preview";
		public const string AutoCloseAutomationName = "Auto-close preview";
		public const string MinimizeAutomationName = "Minimize preview";
		public const string MaximizeAutomationName = "Maximize preview";
		public const string RestoreAutomationName = "Restore preview";
		public const string CloseAutomationName = "Close preview";

		public const string PinToolTip = "Pin: keep this window and its item; the group's next item opens in a new window";
		public const string PinnedToolTip = "Pinned: this window keeps its item until you close it";
		public const string AutoCloseToolTip = "Auto-close: close this group's window when the Focus item it came from is resolved, or when you select another session";
		public const string MinimizeToolTip = "Minimize";
		public const string MaximizeToolTip = "Maximize";
		public const string RestoreToolTip = "Restore";
		public const string CloseToolTip = "Close";

		/// <summary>FR.6.10 / FR.29.1: the Monitor toggle's tooltips; the disabled one is worded by the docs.</summary>
		public const string MonitorDisabledToolTip = "Connect a second monitor to open previews in their own windows";
		public const string MonitorOnToolTip = "Previews open in their own windows. Click to show them beside the session.";
		public const string MonitorOffToolTip = "Previews open beside the session. Click to open them in their own windows.";

		/// <summary>Shown in a file window that was asked to show a file without a read handle.</summary>
		public const string NoFileHandleMessage = "The file could not be opened.";

		/// <summary>A window is saved this long after the last move or resize (as the main window).</summary>
		public static readonly TimeSpan PlacementSaveDelay = TimeSpan.FromMilliseconds(500);

		public const double CaptionHeight = 32;
		public const double ResizeBorder = 6;
		public const double DefaultWidth = 960;
		public const double DefaultHeight = 640;

		/// <summary>Width of the icon button in the caption (the same as the main title bar's tool buttons).</summary>
		public const double ToolButtonWidth = 36;
	}
}
