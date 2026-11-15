using TessynDesktop.Models.Remote;

namespace TessynDesktop.Client;

/// <summary>
/// Tree and node mutation, session options and local tools (P1.J1). Loopback only. The daemon announces them with the capability flags
/// <see cref="RemoteProtocol.TreeMutation.Capability"/>, <see cref="RemoteProtocol.TreeMutation.InfoCapability"/>,
/// <see cref="RemoteProtocol.SessionOptions.Capability"/> and <see cref="RemoteProtocol.LocalTools.Capability"/>. A refusal is a
/// <see cref="TessynRpcException"/> whose message is the reason.
/// </summary>
public partial interface ITessynClient
{
	/// <summary><c>tree.addDirectory</c>: adds a folder as a directory; an existing one is answered as it is.</summary>
	Task<TreeAddDirectoryResult> TreeAddDirectoryAsync(string path, CancellationToken cancellationToken = default);

	/// <summary><c>tree.addGroup</c>: adds a group under a directory or a group.</summary>
	Task<TreeAddGroupResult> TreeAddGroupAsync(string parentId, string name, CancellationToken cancellationToken = default);

	/// <summary><c>tree.renameNode</c>: renames a session or a group.</summary>
	Task<TreeRenameResult> TreeRenameNodeAsync(string nodeId, string name, CancellationToken cancellationToken = default);

	/// <summary><c>tree.deleteNode</c>: deletes a node; <see cref="TreeDeleteResult.BlockedBy"/> says why it stayed. <paramref name="force"/> also deletes a session's transcript file.</summary>
	Task<TreeDeleteResult> TreeDeleteNodeAsync(string nodeId, bool force = false, CancellationToken cancellationToken = default);

	/// <summary><c>tree.moveNode</c>: moves a session under the git-origin rule; <paramref name="index"/> counts the target's top-level sessions (null = first).</summary>
	Task<TreeMoveResult> TreeMoveNodeAsync(string nodeId, string parentId, int? index = null, CancellationToken cancellationToken = default);

	/// <summary><c>tree.moveTargets</c>: the directories and groups a session may be moved into.</summary>
	Task<TreeMoveTargetsResult> TreeMoveTargetsAsync(string nodeId, CancellationToken cancellationToken = default);

	/// <summary><c>tree.nodeInfo</c>: paths, labels and git origins of up to 500 nodes.</summary>
	Task<TreeNodeInfoResult> TreeNodeInfoAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken = default);

	/// <summary><c>tree.refreshGitOrigins</c>: starts a background scan; <c>tree.changed</c> follows when it is done.</summary>
	Task<TreeRefreshGitOriginsResult> TreeRefreshGitOriginsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// <c>session.setOptions</c>: sets the options that are not null; null leaves an option alone (use <see cref="AutoCommitSetting.NoPreference"/> for "No preference").
	/// While the session runs, a change of Auto-Commit or Auto-Document sends the FR.11.12 correction and its notice is in the result.
	/// </summary>
	Task<SessionSetOptionsResult> SessionSetOptionsAsync(string nodeId, AutoCommitSetting? autoCommit = null, bool? autoDocument = null, bool? playCompletionSound = null, CancellationToken cancellationToken = default);

	/// <summary><c>session.resetConversation</c>: detaches the session's Claude session id (FR.11.8). Refused while a turn runs.</summary>
	Task<SessionResetConversationResult> SessionResetConversationAsync(string nodeId, CancellationToken cancellationToken = default);

	/// <summary><c>localTools.get</c>: the Local Tool Profile of a directory and the selectable tools.</summary>
	Task<LocalToolsDto> LocalToolsGetAsync(string directoryId, CancellationToken cancellationToken = default);

	/// <summary><c>localTools.set</c>: stores the Local Tool Profile of a directory and answers it as get does.</summary>
	Task<LocalToolsDto> LocalToolsSetAsync(string directoryId, bool enabled, IReadOnlyList<string> nativeTools, bool includeCmxMcp, IReadOnlyList<string> includedMcpServerIds, CancellationToken cancellationToken = default);
}
