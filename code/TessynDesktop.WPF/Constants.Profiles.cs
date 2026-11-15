namespace TessynDesktop.WPF;

/// <summary>Names and texts of the profile flyout, the profile switch with its cascade dialog and the auth banner (W5, FR.12, ISSUE.013 R1 and R5).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class Profiles
	{
		public const string SwitchCommandId = "session.switchProfile";
		public const string SwitchCommandTitle = "Switch profile…";
		public const string SwitchMenuGroup = "session";
		public const int SwitchMenuOrder = 91;
		public const int FlyoutOrder = 10;
		public const int SwitchButtonOrder = 10;

		/// <summary>Automation names the E2E tests find the controls by.</summary>
		public const string FlyoutButtonName = "Profiles";
		public const string SwitchButtonName = "Switch profile";
		public const string AddButtonName = "Add profile";

		/// <summary>Id of the "switch the session and its descendants" answer of the cascade dialog.</summary>
		public const string CascadeAllId = "all";
		public const string CascadeOnlyId = "only";

		/// <summary>How many descendants the cascade dialog lists before it says "and N more".</summary>
		public const int MaxListedDescendants = 12;

		public const string FlyoutTitle = "Profiles";
		public const string AddTitle = "Add profile";
		public const string AddPrompt = "Name of the new profile (the account label shown in lists):";
		public const string RenameTitle = "Rename profile";
		public const string RenamePrompt = "New name of the profile:";
		public const string RemoveTitle = "Remove profile";
		public const string ConfirmRemove = "Remove the profile \"{0}\"? Its folder is kept on disk with a suffix.";
		public const string RemovedPinned = "Removed \"{0}\". {1} session(s) are pinned to it and will report \"profile missing\" until they are switched.";
		public const string RemovedNoPinned = "Removed \"{0}\".";
		public const string AuthStarted = "Login for \"{0}\" started on the daemon's machine.";
		public const string AuthAlreadyRunning = "A login for \"{0}\" is already running.";
		public const string AuthStateLoggedIn = "logged in";
		public const string AuthStateLoggedOut = "logged out";
		public const string AuthStateUnknown = "system account";
		public const string NameRequired = "Enter a name.";

		public const string PickTitle = "Switch profile";
		public const string PickMessage = "Move \"{0}\" to which profile?";
		public const string CascadeTitle = "Switch profile";
		public const string CascadeMessage = "\"{0}\" has {1} descendant session(s) (workers). Switch them to \"{2}\" too?";
		public const string CascadeAllLabel = "Switch this session and its workers";
		public const string CascadeOnlyLabel = "Switch only this session";
		public const string DescendantIdle = "{0} (idle, on {1})";
		public const string DescendantRunning = "{0} (running, on {1}): queued, applied when its turn ends";
		public const string DescendantMore = "… and {0} more";

		public const string ResultTitle = "Profile switch";
		public const string ResultMoved = "Moved {0} session(s) to \"{1}\".";
		public const string ResultNodeQueued = "\"{0}\" is running: its switch is queued and applied when its turn ends.";
		public const string ResultQueuedHeader = "Queued (running, never interrupted):";
		public const string ResultFailedHeader = "Not moved completely (the switch stays queued for the next turn):";
		public const string ResultOldPaths = "{0} path(s) in the moved transcripts still name the old profile folder.";
		public const string ResultOk = "OK";

		public const string BadgeMismatchTip = "Some workers of this session are on a different profile.";
		public const string BadgeTip = "Profile: {0}";
		public const string BadgeSwitchingTip = "switching to {0} when its turn ends";
		public const string ResultNothingChanged = "Nothing changed: \"{0}\" and its selected workers are already on \"{1}\".";
		public const string DescendantsAtLeast = "at least {0}";
		public const string DefaultProfileLabel = "default";
		public const string DefaultProfileId = "default";
	}
}
