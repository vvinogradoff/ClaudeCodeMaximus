namespace TessynDesktop.WPF;

/// <summary>Texts and limits of the instruction toolbar, the command-bar toggles and the local-tools popup (W4).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class ToolbarTexts
	{
		public const string AutoCommitCommit = "Commit";
		public const string AutoCommitDoNot = "No commit";
		public const string AutoCommitNoPreference = "Commit: no preference";
		public const string AutoCommitCommitTip = "Auto-Commit: commit the changes when the request is done. Click for No preference.";
		public const string AutoCommitDoNotTip = "Auto-Commit: do not commit. Click for Commit.";
		public const string AutoCommitNoPreferenceTip = "Auto-Commit: no commit instruction at all. Click for Do-not-commit.";
		public const string OptionsFailed = "Could not change the session options: ";
		public const string NewBranchNotice = "[New Branch {0} for this run: it applies to the next send]";
		public const string AutoCompactNotice = "[Auto-Compact {0} for this run: it applies to the next send]";
		public const string On = "enabled";
		public const string Off = "disabled";
		public const string ClearTitle = "Terminate session";
		public const string ClearMessage = "Detach the Claude session? Nothing is sent: the next prompt continues from the session's text history.";
		public const string ClearConfirm = "Terminate";
		public const string ClearFailed = "Could not detach the session: ";
		public const string ClearRefused = "The session was not detached (a turn is running).";
		public const string CompactFailed = "Could not compact the session: ";
		public const string StreamingOffTitle = "Turn off Streaming Session Mode";
		public const string StreamingOffMessage = "A streaming process is alive for this session. Turning the mode off terminates it, and any background work it is doing.";
		public const string StreamingOffConfirm = "Turn off";
		public const string StreamingFailed = "Could not change Streaming Session Mode: ";
		public const string LocalToolsFailed = "Could not change the local tools: ";
		public const string LocalToolsOnTip = "Local tools: on. Click to turn off; hold to choose the tools.";
		public const string LocalToolsOffTip = "Local tools: off (the full tools and MCP apply). Click to turn on; hold to choose the tools.";
	}

	public static class ToolbarLimits
	{
		/// <summary>How long the local-tools button is held before it opens the checklist instead of toggling the master switch (FR.12.23.2).</summary>
		public static readonly TimeSpan LocalToolsHold = TimeSpan.FromMilliseconds(450);
	}
}
