namespace TessynDesktop;

/// <summary>Limits and refusal texts of the tree mutation methods (P1.J1). One file per package keeps the shared constants file free of merge conflicts.</summary>
/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	public static class TreeMutation
	{
		/// <summary>The most node ids one <c>tree.nodeInfo</c> call accepts.</summary>
		public const int NodeInfoMaxIds = 500;

		/// <summary>How long the daemon trusts "this directory has no git origin" before it reads the folder again.</summary>
		public const int NoOriginCacheMinutes = 5;

		public const string NodeNotFound            = "Node not found";
		public const string DirectoryNotFound       = "Directory not found";
		public const string ParentNotFound          = "Target not found";
		public const string NameRequired            = "'name' must not be blank";
		public const string FolderRefused           = "That folder cannot be added";
		public const string PathNotFound            = "The folder does not exist";
		public const string DirectoryRenameRefused  = "A directory cannot be renamed; its name comes from its path";
		public const string OnlySessionsMove        = "Only sessions can be moved";
		public const string GroupMoveRefused        = "A group can only be moved within its own parent";
		public const string WorkerMoveRefused       = "A worker session cannot be moved; it follows its supervisor";
		public const string TargetMustBeContainer   = "The target must be a directory or a group";
		public const string GitOriginRefused        = "Cannot move here: the target belongs to a different git repository (or none) than the session's directory";
		public const string SessionRunning          = "The session is running; stop it first";
		public const string NotEmpty                = "The node is not empty";
		public const string SessionFileExists       = "The session has a transcript file; delete with force to remove it";
		public const string HasWorkers              = "The session has worker sessions; delete with force";
		public const string TooManyNodeIds          = "'nodeIds' holds more than 500 ids";
		public const string TurnRunning             = "A turn is running; stop it first";
		public const string UnknownNativeTool       = "Unknown native tool";
	}
}
