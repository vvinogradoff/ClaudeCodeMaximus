using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>
/// The tree mutation, session option and local tool methods of the fake (P1.J1). Every call is recorded in <see cref="Calls"/> as "method:argument";
/// each method answers from its handler when one is set, otherwise with a plain success. A handler may throw to script a refusal.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FakeTessynClient
{
	public Func<string, TreeAddDirectoryResult>? AddDirectoryHandler { get; set; }

	public Func<string, string, TreeAddGroupResult>? AddGroupHandler { get; set; }

	public Func<string, string, TreeRenameResult>? RenameNodeHandler { get; set; }

	public Func<string, bool, TreeDeleteResult>? DeleteNodeHandler { get; set; }

	public Func<string, string, int?, TreeMoveResult>? MoveNodeHandler { get; set; }

	public Func<string, TreeMoveTargetsResult>? MoveTargetsHandler { get; set; }

	public Func<IReadOnlyList<string>, TreeNodeInfoResult>? NodeInfoHandler { get; set; }

	public Func<TreeRefreshGitOriginsResult>? RefreshGitOriginsHandler { get; set; }

	public Func<string, AutoCommitSetting?, bool?, bool?, SessionSetOptionsResult>? SetOptionsHandler { get; set; }

	public Func<string, SessionResetConversationResult>? ResetConversationHandler { get; set; }

	public Func<string, LocalToolsDto>? LocalToolsGetHandler { get; set; }

	public Func<string, bool, IReadOnlyList<string>, bool, IReadOnlyList<string>, LocalToolsDto>? LocalToolsSetHandler { get; set; }

	public Task<TreeAddDirectoryResult> TreeAddDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
		Record($"tree.addDirectory:{path}", () => AddDirectoryHandler?.Invoke(path) ?? new TreeAddDirectoryResult("dir-" + path, Path.GetFileName(path)));

	public Task<TreeAddGroupResult> TreeAddGroupAsync(string parentId, string name, CancellationToken cancellationToken = default) =>
		Record($"tree.addGroup:{parentId}:{name}", () => AddGroupHandler?.Invoke(parentId, name) ?? new TreeAddGroupResult("group-" + name));

	public Task<TreeRenameResult> TreeRenameNodeAsync(string nodeId, string name, CancellationToken cancellationToken = default) =>
		Record($"tree.renameNode:{nodeId}:{name}", () => RenameNodeHandler?.Invoke(nodeId, name) ?? new TreeRenameResult(name.Trim()));

	public Task<TreeDeleteResult> TreeDeleteNodeAsync(string nodeId, bool force = false, CancellationToken cancellationToken = default) =>
		Record($"tree.deleteNode:{nodeId}:{force}", () => DeleteNodeHandler?.Invoke(nodeId, force) ?? new TreeDeleteResult(true, null));

	public Task<TreeMoveResult> TreeMoveNodeAsync(string nodeId, string parentId, int? index = null, CancellationToken cancellationToken = default) =>
		Record($"tree.moveNode:{nodeId}:{parentId}:{index}", () => MoveNodeHandler?.Invoke(nodeId, parentId, index) ?? new TreeMoveResult(true));

	public Task<TreeMoveTargetsResult> TreeMoveTargetsAsync(string nodeId, CancellationToken cancellationToken = default) =>
		Record($"tree.moveTargets:{nodeId}", () => MoveTargetsHandler?.Invoke(nodeId) ?? new TreeMoveTargetsResult([]));

	public Task<TreeNodeInfoResult> TreeNodeInfoAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken = default) =>
		Record($"tree.nodeInfo:{string.Join(",", nodeIds)}", () => NodeInfoHandler?.Invoke(nodeIds) ?? new TreeNodeInfoResult([]));

	public Task<TreeRefreshGitOriginsResult> TreeRefreshGitOriginsAsync(CancellationToken cancellationToken = default) =>
		Record("tree.refreshGitOrigins:", () => RefreshGitOriginsHandler?.Invoke() ?? new TreeRefreshGitOriginsResult(true));

	public Task<SessionSetOptionsResult> SessionSetOptionsAsync(string nodeId, AutoCommitSetting? autoCommit = null, bool? autoDocument = null, bool? playCompletionSound = null, CancellationToken cancellationToken = default) =>
		Record($"session.setOptions:{nodeId}:{autoCommit}:{autoDocument}:{playCompletionSound}", () =>
			SetOptionsHandler?.Invoke(nodeId, autoCommit, autoDocument, playCompletionSound)
			?? new SessionSetOptionsResult(new SessionOptionsDto(autoCommit switch { AutoCommitSetting.Commit => true, AutoCommitSetting.DoNotCommit => false, _ => null }, autoDocument ?? false, playCompletionSound ?? true), null));

	public Task<SessionResetConversationResult> SessionResetConversationAsync(string nodeId, CancellationToken cancellationToken = default) =>
		Record($"session.resetConversation:{nodeId}", () => ResetConversationHandler?.Invoke(nodeId) ?? new SessionResetConversationResult(true));

	public Task<LocalToolsDto> LocalToolsGetAsync(string directoryId, CancellationToken cancellationToken = default) =>
		Record($"localTools.get:{directoryId}", () => LocalToolsGetHandler?.Invoke(directoryId) ?? new LocalToolsDto(true, [], false, [], [], []));

	public Task<LocalToolsDto> LocalToolsSetAsync(string directoryId, bool enabled, IReadOnlyList<string> nativeTools, bool includeCmxMcp, IReadOnlyList<string> includedMcpServerIds, CancellationToken cancellationToken = default) =>
		Record($"localTools.set:{directoryId}", () =>
			LocalToolsSetHandler?.Invoke(directoryId, enabled, nativeTools, includeCmxMcp, includedMcpServerIds)
			?? new LocalToolsDto(enabled, nativeTools, includeCmxMcp, includedMcpServerIds, [], []));

	/// <summary>Records the call, then answers from <paramref name="answer"/>; an exception it throws comes back as a faulted task.</summary>
	private Task<T> Record<T>(string call, Func<T> answer)
	{
		lock (_gate)
			Calls.Add(call);

		try
		{
			return Task.FromResult(answer());
		}
		catch (Exception ex)
		{
			return Task.FromException<T>(ex);
		}
	}
}
