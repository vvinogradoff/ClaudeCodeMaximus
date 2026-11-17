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

		/// <summary>Main window, extra tabs of the left panel next to Tree: Recent, Tags (DataContext: MainViewModel).</summary>
		public const string LeftPanelTabs = "main.leftPanelTabs";

		/// <summary>Main window, items at the right of the status bar (DataContext: MainViewModel).</summary>
		public const string StatusBarItems = "main.statusBarItems";

		/// <summary>Session pane, buttons on the right of the header (DataContext: SessionPaneViewModel).</summary>
		public const string SessionHeaderToolbar = "session.headerToolbar";

		/// <summary>Session pane, the row of instruction toggles above the prompt input (DataContext: SessionPaneViewModel).</summary>
		public const string InstructionToolbar = "session.instructionToolbar";

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
		public const int ScreensaverTimeoutSeconds = 120;
		public const int ScreensaverSlideshowIntervalSeconds = 10;

		/// <summary>The client waits for a daemon instead of starting the background host (side-by-side with the Avalonia window).</summary>
		public const bool StartBackgroundDaemon = false;
	}

	/// <summary>Labels of the dialogs.</summary>
	public static class DialogTexts
	{
		public const string Ok = "OK";
		public const string Cancel = "Cancel";
		public const string Confirm = "Confirm";
	}
}
