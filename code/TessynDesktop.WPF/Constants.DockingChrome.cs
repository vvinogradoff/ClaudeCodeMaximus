namespace TessynDesktop.WPF;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>The compact docking chrome, its keyboard routing and its focus rule (FR.6.13, FR.6.20).</summary>
	public static class DockingChrome
	{
		/// <summary>Resource keys of <c>Styles/DockingChrome.xaml</c>.</summary>
		public const string PaneStyleKey = "DockingChromeAnchorablePaneControlStyle";
		public const string AnchorableMenuKey = "DockingChromeAnchorableContextMenu";
		public const string DocumentMenuKey = "DockingChromeDocumentContextMenu";

		/// <summary>The library's internal method that hides the slide-out; the only way to close it from outside.</summary>
		public const string HideAutoHideWindowMethod = "HideAutoHideWindow";

		/// <summary>A focus move into another panel within this long after a layout change, with no user input in that time, is not the user's (FR.6.20).</summary>
		public static readonly TimeSpan FocusStealWindow = TimeSpan.FromMilliseconds(400);
	}
}
