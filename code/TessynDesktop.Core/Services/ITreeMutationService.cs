using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The daemon side of the tree mutation methods (P1.J1): validation, the git-origin move rule and the calls into <see cref="ISessionRegistry"/>.
/// A refusal throws <see cref="TdcpException"/> (the reason is the message); a malformed argument throws <see cref="TdcpInvalidParamsException"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITreeMutationService
{
	/// <summary>Adds <paramref name="path"/> as a directory; an existing one is answered as it is. The folder must exist.</summary>
	TreeAddDirectoryResult AddDirectory(string path);

	/// <summary>Adds a group under a directory or a group.</summary>
	TreeAddGroupResult AddGroup(string parentId, string name);

	/// <summary>Renames a session or a group.</summary>
	TreeRenameResult RenameNode(string nodeId, string name);

	/// <summary>Deletes a node when nothing blocks it; otherwise answers why not (<see cref="RemoteProtocol.TreeMutation.BlockedBy"/>).</summary>
	TreeDeleteResult DeleteNode(string nodeId, bool force);

	/// <summary>Moves a top-level session into a directory or group at <paramref name="index"/> (null = first), under the git-origin rule.</summary>
	TreeMoveResult MoveNode(string nodeId, string parentId, int? index);

	/// <summary>The directories and groups the session may be moved into under the git-origin rule.</summary>
	TreeMoveTargetsResult MoveTargets(string nodeId);

	/// <summary>Paths, labels and git origins of the nodes; unknown ids are left out.</summary>
	TreeNodeInfoResult NodeInfo(IReadOnlyList<string> nodeIds);

	/// <summary>Starts a background scan of every directory's git origin; <c>tree.changed</c> follows. Returns the scan's task, or null when one already runs.</summary>
	Task? RefreshGitOrigins();
}
