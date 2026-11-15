namespace TessynDesktop.WPF;

/// <summary>Command ids, texts and limits of the tree operations (W2).</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>Ids of the tree commands in the command registry (<c>IShellCommands</c>).</summary>
	public static class TreeCommands
	{
		public const string AddDirectory = "tree.addDirectory";
		public const string AddGroup = "tree.addGroup";
		public const string AddSession = "tree.addSession";
		public const string Rename = "tree.rename";
		public const string Delete = "tree.delete";
		public const string MoveTo = "tree.moveTo";
		public const string RefreshGitOrigins = "tree.refreshGitOrigins";
	}

	/// <summary>Automation names of the tree toolbar's buttons (UI Automation finds them by these).</summary>
	public static class TreeAutomation
	{
		public const string AddDirectory = "Add directory";
		public const string AddGroup = "New group";
		public const string AddSession = "New session";
		public const string Rename = "Rename";
		public const string Delete = "Delete";
		public const string MoveTo = "Move to";
		public const string RefreshGitOrigins = "Refresh git origins";
	}

	/// <summary>Limits of the tree operations.</summary>
	public static class TreeLimits
	{
		/// <summary>The most nodes one <c>tree.nodeInfo</c> call may ask about (the daemon refuses more).</summary>
		public const int NodeInfoBatch = 500;
	}

	/// <summary>Texts of the tree dialogs and feedback; tests refer to them too.</summary>
	public static class TreeTexts
	{
		public const string AddDirectoryTitle = "Add directory";
		public const string AddDirectoryFailed = "Cannot add this directory";
		public const string NewGroupTitle = "New group";
		public const string NewGroupPrompt = "Name of the new group:";
		public const string NewGroupFailed = "Cannot add the group";
		public const string NewSessionTitle = "New session";
		public const string NewSessionPrompt = "Name of the new session:";
		public const string NewSessionDefaultName = "New session";
		public const string NewSessionFailed = "Cannot add the session";
		public const string NodeGone = "That item was removed meanwhile (by another window or client). The tree has been refreshed.";
		public const string MoveNotDone = "The session was not moved.";
		public const string ConfirmDeleteDirectory = "Remove the directory \"{0}\" from the tree? This removes its entry and the settings Tessyn keeps for it (such as its local tool profile). The folder and its files on disk are not touched.";
		public const string ConfirmDeleteGroup = "Delete the group \"{0}\"? Only an empty group can be deleted; its entry in the tree is removed.";
		public const string RenameTitle = "Rename";
		public const string RenamePrompt = "New name:";
		public const string RenameFailed = "Cannot rename";
		public const string NameRequired = "A name is required.";
		public const string DeleteTitle = "Delete";
		public const string DeleteFailed = "Cannot delete";
		public const string DeleteButton = "Delete";
		public const string MoveTitle = "Move to";
		public const string MoveMessage = "Move \"{0}\" into:";
		public const string MoveFailed = "Cannot move";
		public const string NowhereToMove = "This session cannot be moved anywhere else.";
		public const string RefreshFailed = "Cannot refresh the git origins";
		public const string RefreshAlreadyRunning = "A git origin scan is already running.";
		public const string Close = "Close";
		public const string Ok = "OK";
		public const string GitOriginPrefix = "Origin: ";

		/// <summary>The drag feedback when the git-origin rule refuses a target (A9, FR.1).</summary>
		public const string OtherRepository = "Cannot move here: other repository";
		public const string DropOnContainer = "Drop onto a directory or a group.";
		public const string SessionRunning = "The session is running; stop it first.";
		public const string MoveTargetsPending = "Checking where this session can go…";
		public const string NotMovable = "Only sessions can be moved.";
		public const string MoveTargetsFailed = "Cannot check where this session can go.";

		public const string BlockedSessionFile = "\"{0}\" has a transcript file. Deleting it removes the file too and cannot be undone.";
		public const string BlockedWorkers = "\"{0}\" has worker sessions. Deleting it deletes them as well.";
		public const string BlockedRunning = "\"{0}\" is running. Stop it first, then delete it.";
		public const string BlockedNotEmpty = "\"{0}\" is not empty. Move or delete what is in it first.";
		public const string BlockedUnknown = "\"{0}\" cannot be deleted ({1}).";
	}
}
