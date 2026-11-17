namespace TessynDesktop.WPF;

/// <summary>Names of the shell's extension points and defaults of the client settings sections (W0).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Ids of the empty layout slots later packages contribute views to (see <c>IShellSlots</c>).</summary>
	public static class Slots
	{
		/// <summary>Main window, a row between the connection banner and the content (DataContext: MainViewModel).</summary>
		public const string TitleBarToolbar = "main.titleBarToolbar";

		/// <summary>Main window, the title bar's middle: the one view in it takes all the width between the toolbar and the build stamp (the Recent dropdown) (DataContext: MainViewModel).</summary>
		public const string TitleBarCenter = "main.titleBarCenter";

		/// <summary>Main window, extra tabs of the left panel next to Tree: Recent, Tags (DataContext: MainViewModel).</summary>
		public const string LeftPanelTabs = "main.leftPanelTabs";

		/// <summary>Main window, the status bar's middle: the one view in it takes all the width the session count leaves (model line, usage meters) (DataContext: MainViewModel).</summary>
		public const string StatusBarItems = "main.statusBarItems";

		/// <summary>Main window, the far right of the status bar, after the item that fills the width (the Profiles button) (DataContext: MainViewModel).</summary>
		public const string StatusBarRight = "main.statusBarRight";

		/// <summary>Session pane, buttons on the right of the header (DataContext: SessionPaneViewModel).</summary>
		public const string SessionHeaderToolbar = "session.headerToolbar";

		/// <summary>Session pane, above the prompt box: autocomplete hints, draft and auth notices (DataContext: SessionPaneViewModel).</summary>
		public const string ComposerAccessory = "session.composerAccessory";

		/// <summary>Session pane, below the prompt box: model, effort and profile selectors, toggles (DataContext: SessionPaneViewModel).</summary>
		public const string CommandBar = "session.commandBar";

		/// <summary>Tree panel, above the search box (DataContext: TreeViewModel).</summary>
		public const string TreeToolbar = "tree.toolbar";

		/// <summary>Tree panel, below the rows (DataContext: TreeViewModel).</summary>
		public const string TreeFooter = "tree.footer";
	}

	/// <summary>Ids of the menus built from the command registry.</summary>
	public static class Menus
	{
		/// <summary>The tree's context menu; the command parameter is the <c>TreeNodeViewModel</c> under the mouse.</summary>
		public const string TreeNode = "tree.node";

		/// <summary>The hamburger menu of the title bar (Import, Settings); Exit is added by the title bar itself. The command parameter is null.</summary>
		public const string Main = "main.menu";
	}

	/// <summary>Names of the sections in the client settings store (<c>wpf.json</c>, <c>sections</c>).</summary>
	public static class SettingsSections
	{
		public const string Appearance = "appearance";
		public const string KeyBindings = "keyBindings";
		public const string Screensaver = "screensaver";
		public const string Layout = "layout";
		public const string DaemonHost = "daemonHost";
		public const string Colours = "colours";
	}

	/// <summary>Defaults of the client settings sections; they mirror the Avalonia client's.</summary>
	public static class Defaults
	{
		public const double FontSize = 13.0;

		/// <summary>Line spacing multiplier (FR.9.12): the Avalonia client's natural Segoe UI line is about 1.33 and the transcript page used 1.45; the default is tighter than both.</summary>
		public const double LineSpacing = 1.25;

		/// <summary>The tag chips and the "+N" hint of the session tag area: the size they had (Fluent's 14) before it became a setting.</summary>
		public const double TagFontSize = 14.0;
		public const int ScreensaverTimeoutSeconds = 120;
		public const int ScreensaverSlideshowIntervalSeconds = 10;

		/// <summary>WPF is the only client: it starts the background daemon host itself when none runs (it was off while the Avalonia window ran side by side). A stored file without the key gets this value too.</summary>
		public const bool StartBackgroundDaemon = true;
	}

	/// <summary>Labels of the dialogs.</summary>
	public static class DialogTexts
	{
		public const string Ok = "OK";
		public const string Cancel = "Cancel";
		public const string Confirm = "Confirm";
	}
}
