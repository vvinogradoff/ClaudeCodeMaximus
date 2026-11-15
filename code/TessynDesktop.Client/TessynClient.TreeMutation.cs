using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Client;

public sealed partial class TessynClient
{
	/// <inheritdoc />
	public Task<TreeAddDirectoryResult> TreeAddDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
		CallAsync<TreeAddDirectoryResult>(RemoteProtocol.TreeMutation.AddDirectoryMethod, Params(("path", Node(path))), cancellationToken);

	/// <inheritdoc />
	public Task<TreeAddGroupResult> TreeAddGroupAsync(string parentId, string name, CancellationToken cancellationToken = default) =>
		CallAsync<TreeAddGroupResult>(RemoteProtocol.TreeMutation.AddGroupMethod, Params(("parentId", Node(parentId)), ("name", Node(name))), cancellationToken);

	/// <inheritdoc />
	public Task<TreeRenameResult> TreeRenameNodeAsync(string nodeId, string name, CancellationToken cancellationToken = default) =>
		CallAsync<TreeRenameResult>(RemoteProtocol.TreeMutation.RenameNodeMethod, Params(("nodeId", Node(nodeId)), ("name", Node(name))), cancellationToken);

	/// <inheritdoc />
	public Task<TreeDeleteResult> TreeDeleteNodeAsync(string nodeId, bool force = false, CancellationToken cancellationToken = default) =>
		CallAsync<TreeDeleteResult>(RemoteProtocol.TreeMutation.DeleteNodeMethod, Params(("nodeId", Node(nodeId)), ("force", Node(force))), cancellationToken);

	/// <inheritdoc />
	public Task<TreeMoveResult> TreeMoveNodeAsync(string nodeId, string parentId, int? index = null, CancellationToken cancellationToken = default) =>
		CallAsync<TreeMoveResult>(RemoteProtocol.TreeMutation.MoveNodeMethod, Params(("nodeId", Node(nodeId)), ("parentId", Node(parentId)), ("index", Node(index))), cancellationToken);

	/// <inheritdoc />
	public Task<TreeMoveTargetsResult> TreeMoveTargetsAsync(string nodeId, CancellationToken cancellationToken = default) =>
		CallAsync<TreeMoveTargetsResult>(RemoteProtocol.TreeMutation.MoveTargetsMethod, Params(("nodeId", Node(nodeId))), cancellationToken);

	/// <inheritdoc />
	public Task<TreeNodeInfoResult> TreeNodeInfoAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken = default) =>
		CallAsync<TreeNodeInfoResult>(RemoteProtocol.TreeMutation.NodeInfoMethod, Params(("nodeIds", Strings(nodeIds))), cancellationToken);

	/// <inheritdoc />
	public Task<TreeRefreshGitOriginsResult> TreeRefreshGitOriginsAsync(CancellationToken cancellationToken = default) =>
		CallAsync<TreeRefreshGitOriginsResult>(RemoteProtocol.TreeMutation.RefreshGitOriginsMethod, null, cancellationToken);

	/// <inheritdoc />
	public Task<SessionSetOptionsResult> SessionSetOptionsAsync(string nodeId, AutoCommitSetting? autoCommit = null, bool? autoDocument = null, bool? playCompletionSound = null, CancellationToken cancellationToken = default)
	{
		var parameters = Params(("nodeId", Node(nodeId)), ("autoDocument", Node(autoDocument)), ("playCompletionSound", Node(playCompletionSound)));

		// An explicit JSON null is "No preference", so this one key cannot go through Params (which leaves nulls out).
		if (autoCommit is { } setting)
			parameters["autoCommit"] = setting == AutoCommitSetting.NoPreference ? null : JsonValue.Create(setting == AutoCommitSetting.Commit);

		return CallAsync<SessionSetOptionsResult>(RemoteProtocol.SessionOptions.SetMethod, parameters, cancellationToken);
	}

	/// <inheritdoc />
	public Task<SessionResetConversationResult> SessionResetConversationAsync(string nodeId, CancellationToken cancellationToken = default) =>
		CallAsync<SessionResetConversationResult>(RemoteProtocol.SessionOptions.ResetMethod, Params(("nodeId", Node(nodeId))), cancellationToken);

	/// <inheritdoc />
	public Task<LocalToolsDto> LocalToolsGetAsync(string directoryId, CancellationToken cancellationToken = default) =>
		CallAsync<LocalToolsDto>(RemoteProtocol.LocalTools.GetMethod, Params(("directoryId", Node(directoryId))), cancellationToken);

	/// <inheritdoc />
	public Task<LocalToolsDto> LocalToolsSetAsync(string directoryId, bool enabled, IReadOnlyList<string> nativeTools, bool includeCmxMcp, IReadOnlyList<string> includedMcpServerIds, CancellationToken cancellationToken = default) =>
		CallAsync<LocalToolsDto>(
			RemoteProtocol.LocalTools.SetMethod,
			Params(
				("directoryId", Node(directoryId)),
				("enabled", Node(enabled)),
				("nativeTools", Strings(nativeTools)),
				("includeCmxMcp", Node(includeCmxMcp)),
				("includedMcpServerIds", Strings(includedMcpServerIds))),
			cancellationToken);

	private static JsonNode Strings(IReadOnlyList<string> values) => JsonSerializer.SerializeToNode(values, TdcpJsonOptions.Default)!;
}
