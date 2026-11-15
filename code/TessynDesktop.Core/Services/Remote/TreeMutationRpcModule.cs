using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The tree mutation methods of a local client (P1.J1): <c>tree.addDirectory</c>, <c>tree.addGroup</c>, <c>tree.renameNode</c>,
/// <c>tree.deleteNode</c>, <c>tree.moveNode</c>, <c>tree.moveTargets</c>, <c>tree.nodeInfo</c> and <c>tree.refreshGitOrigins</c>. All are loopback
/// only (<c>remoteCallable: false</c>): they change the tree or hand out local paths. The logic is <see cref="ITreeMutationService"/>; a change
/// reaches every client as <c>tree.changed</c>, raised by the registry.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeMutationRpcModule : IRemoteRpcModule
{
	private readonly ITreeMutationService _tree;

	public TreeMutationRpcModule(ITreeMutationService tree)
	{
		_tree = tree;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(RemoteProtocol.TreeMutation.AddDirectoryMethod,       AddDirectory,       remoteCallable: false);
		registry.AddSync(RemoteProtocol.TreeMutation.AddGroupMethod,           AddGroup,           remoteCallable: false);
		registry.AddSync(RemoteProtocol.TreeMutation.RenameNodeMethod,         RenameNode,         remoteCallable: false);
		registry.AddSync(RemoteProtocol.TreeMutation.DeleteNodeMethod,         DeleteNode,         remoteCallable: false);
		registry.AddSync(RemoteProtocol.TreeMutation.MoveNodeMethod,           MoveNode,           remoteCallable: false);
		registry.AddSync(RemoteProtocol.TreeMutation.MoveTargetsMethod,        MoveTargets,        remoteCallable: false);
		registry.AddSync(RemoteProtocol.TreeMutation.NodeInfoMethod,           NodeInfo,           remoteCallable: false);
		registry.AddSync(RemoteProtocol.TreeMutation.RefreshGitOriginsMethod,  RefreshGitOrigins,  remoteCallable: false);
	}

	private object? AddDirectory(JsonNode? @params) =>
		_tree.AddDirectory(TdcpParams.RequireString(@params, "path"));

	private object? AddGroup(JsonNode? @params) =>
		_tree.AddGroup(TdcpParams.RequireString(@params, "parentId"), TdcpParams.RequireString(@params, "name"));

	private object? RenameNode(JsonNode? @params) =>
		_tree.RenameNode(TdcpParams.RequireString(@params, "nodeId"), TdcpParams.RequireString(@params, "name"));

	private object? DeleteNode(JsonNode? @params) =>
		_tree.DeleteNode(TdcpParams.RequireString(@params, "nodeId"), TdcpParams.OptionalBool(@params, "force") ?? false);

	private object? MoveNode(JsonNode? @params) =>
		_tree.MoveNode(TdcpParams.RequireString(@params, "nodeId"), TdcpParams.RequireString(@params, "parentId"), TdcpParams.OptionalInt(@params, "index"));

	private object? MoveTargets(JsonNode? @params) =>
		_tree.MoveTargets(TdcpParams.RequireString(@params, "nodeId"));

	private object? NodeInfo(JsonNode? @params) =>
		_tree.NodeInfo(TdcpListParams.RequireStrings(@params, "nodeIds"));

	/// <summary>The scan runs in the background (plan 0.10 precedent): the answer only says whether one was started.</summary>
	private object? RefreshGitOrigins(JsonNode? @params) =>
		new TreeRefreshGitOriginsResult(_tree.RefreshGitOrigins() != null);
}
