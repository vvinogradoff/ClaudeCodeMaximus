using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF;

/// <summary>Defaults, limits and timings of the preview windows (FR.29).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class Previews
	{
		/// <summary>The groups of a client that never edited the list, in this order (auto-close off).</summary>
		public static readonly IReadOnlyList<PreviewGroupDefault> DefaultGroups =
		[
			new("images", "Images", "jpg jpeg png gif webp bmp tif tiff"),
			new("text-code", "Text and code", "md txt h cpp"),
			new("csv", "CSV", "csv"),
			new("slack", "Slack", "https://hardstop.slack.com/*"),
			new("windows-app", "Windows app", "https://github.com/tryhardstop/windows-app/*"),
			new("backend-frontend", "Backend and frontend", "https://github.com/tryhardstop/backend-v1/* https://github.com/tryhardstop/frontend-v1/*"),
			new("linear", "Linear", "https://linear.app/hardstop/issue/*"),
			new("web", "Web", "https://*"),
		];

		/// <summary>The most characters an extension pattern may have (without the dot).</summary>
		public const int MaxExtensionLength = 16;

		public const string HttpPrefix = "http://";
		public const string HttpsPrefix = "https://";
		public const char Wildcard = '*';

		/// <summary>The smallest a preview window may be (the main window's minimum is larger).</summary>
		public const int MinWindowWidth = 360;
		public const int MinWindowHeight = 240;

		/// <summary>A first window takes this share of the target monitor's work area, each way.</summary>
		public const int FirstWindowPercent = 60;

		/// <summary>The monitor list is read once the display settings stayed quiet for this long.</summary>
		public static readonly TimeSpan MonitorDebounce = TimeSpan.FromSeconds(1);

		/// <summary>Windows do not save their placement until this long after the debounced monitor update (Windows moves them by itself meanwhile).</summary>
		public static readonly TimeSpan MonitorSettle = TimeSpan.FromSeconds(2);

		/// <summary>Test seam (<c>--test-monitors</c>): the accepted monitor counts, the work area of each monitor and the device name prefix.</summary>
		public const int TestMonitorsMin = 1;
		public const int TestMonitorsMax = 4;
		public const int TestMonitorWidth = 1920;
		public const int TestMonitorHeight = 1080;
		public const string TestMonitorDevicePrefix = @"\\.\TESTDISPLAY";
	}
}
