using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Tests.Contract;
using Xunit;

namespace TessynDesktop.Tests.Client;

/// <summary>The J1 wrappers of <see cref="TessynClient"/> (tree mutation, session options, local tools) against a real loopback daemon.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeMutationClientTests : IDisposable
{
	private readonly TreeMutationWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	private async Task<TessynClient> ConnectAsync()
	{
		var client = new TessynClient(new Uri(_env.Host.BaseUrl + Constants.Agent.TdcpEndpointPath), TdcpTestHost.Token);
		await client.ConnectAsync();
		return client;
	}

	[Fact]
	public async Task Connect_SeesTheJ1CapabilityFlags()
	{
		await using var client = await ConnectAsync();

		var capabilities = client.ServerInfo!.Capabilities;
		Assert.Contains(RemoteProtocol.TreeMutation.Capability, capabilities);
		Assert.Contains(RemoteProtocol.TreeMutation.InfoCapability, capabilities);
		Assert.Contains(RemoteProtocol.SessionOptions.Capability, capabilities);
		Assert.Contains(RemoteProtocol.LocalTools.Capability, capabilities);
	}

	[Fact]
	public async Task TheTreeMethods_AddRenameMoveAndDelete_ThroughTheTypedWrappers()
	{
		await using var client = await ConnectAsync();
		var folder = Path.Combine(_env.Root, "added");
		Directory.CreateDirectory(folder);

		var directory = await client.TreeAddDirectoryAsync(folder);
		var group     = await client.TreeAddGroupAsync(directory.DirectoryId, "  Group ");
		var renamed   = await client.TreeRenameNodeAsync(group.GroupId, "Renamed");
		var targets   = await client.TreeMoveTargetsAsync(_env.Session.NodeId);
		var moved     = await client.TreeMoveNodeAsync(_env.Session.NodeId, group.GroupId, index: 0);

		Assert.Equal(folder, directory.Label);
		Assert.Equal("Renamed", renamed.Name);
		Assert.Contains(group.GroupId, targets.ParentIds);
		Assert.True(moved.Moved);
		Assert.Equal(_env.Session.NodeId, Assert.Single(_env.Turn.Registry.FindGroupByNodeId(group.GroupId)!.Sessions).NodeId);

		var blocked = await client.TreeDeleteNodeAsync(group.GroupId);
		Assert.Equal((false, RemoteProtocol.TreeMutation.BlockedBy.NotEmpty), (blocked.Deleted, blocked.BlockedBy));
	}

	[Fact]
	public async Task TreeDeleteNodeAsync_AnEmptyDirectoryGoes_ASessionWithAFileNeedsForce()
	{
		await using var client = await ConnectAsync();
		var empty = _env.AddDirectory("empty");

		var removed = await client.TreeDeleteNodeAsync(empty.NodeId);
		var blocked = await client.TreeDeleteNodeAsync(_env.Session.NodeId);
		var forced  = await client.TreeDeleteNodeAsync(_env.Session.NodeId, force: true);

		Assert.True(removed.Deleted);
		Assert.Equal(RemoteProtocol.TreeMutation.BlockedBy.SessionFile, blocked.BlockedBy);
		Assert.True(forced.Deleted);
	}

	[Fact]
	public async Task ARefusedCall_IsATypedRpcException_WithTheReasonAsMessage()
	{
		await using var client = await ConnectAsync();
		var other = _env.AddDirectory("other");
		_env.Origins.Origins[_env.Directory.Path] = "git@github.com:org/repo.git";

		var refusedMove = await Assert.ThrowsAsync<TessynRpcException>(() => client.TreeMoveNodeAsync(_env.Session.NodeId, other.NodeId));
		var blankName   = await Assert.ThrowsAsync<TessynRpcException>(() => client.TreeAddGroupAsync(_env.Directory.NodeId, "  "));

		Assert.Equal((-32000, Constants.TreeMutation.GitOriginRefused), (refusedMove.Code, refusedMove.Message));
		Assert.Equal(-32602, blankName.Code);
	}

	[Fact]
	public async Task TreeNodeInfoAndRefresh_AnswerPathsAndStartAScan()
	{
		await using var client = await ConnectAsync();
		_env.Origins.Origins[_env.Directory.Path] = "git@github.com:org/repo.git";

		var info    = await client.TreeNodeInfoAsync([_env.Session.NodeId, "unknown"]);
		var refresh = await client.TreeRefreshGitOriginsAsync();

		var item = Assert.Single(info.Items);
		Assert.Equal(("session", "Wire session", _env.Directory.Path, "git@github.com:org/repo.git"), (item.Kind, item.Path, item.WorkingDirectory, item.GitOrigin));
		Assert.True(refresh.Started);
	}

	[Fact]
	public async Task SessionSetOptionsAsync_TheThreeAutoCommitStatesAndTheOtherOptions_ReachTheSession()
	{
		await using var client = await ConnectAsync();
		var id = _env.Session.NodeId;

		var commit = await client.SessionSetOptionsAsync(id, AutoCommitSetting.Commit);
		Assert.True(commit.Options.AutoCommit);

		var doNot = await client.SessionSetOptionsAsync(id, AutoCommitSetting.DoNotCommit, autoDocument: true, playCompletionSound: false);
		Assert.Equal(new SessionOptionsDto(false, true, false), doNot.Options);

		var none = await client.SessionSetOptionsAsync(id, AutoCommitSetting.NoPreference);
		Assert.Null(none.Options.AutoCommit);
		Assert.Null(_env.Session.IsAutoCommit);

		var untouched = await client.SessionSetOptionsAsync(id, playCompletionSound: true);
		Assert.Null(untouched.Options.AutoCommit);
		Assert.True(untouched.Options.AutoDocument);
		Assert.True(untouched.Options.PlayCompletionSound);
		Assert.Null(untouched.Corrections);
	}

	[Fact]
	public async Task SessionResetConversationAsync_DetachesTheClaudeSessionId()
	{
		await using var client = await ConnectAsync();
		_env.Session.ClaudeSessionId = "claude-1";

		var first  = await client.SessionResetConversationAsync(_env.Session.NodeId);
		var second = await client.SessionResetConversationAsync(_env.Session.NodeId);

		Assert.True(first.Cleared);
		Assert.False(second.Cleared);
		Assert.Null(_env.Session.ClaudeSessionId);
	}

	[Fact]
	public async Task LocalToolsAsync_GetAndSet_RoundTripTheProfile_AndRefuseAnUnknownTool()
	{
		await using var client = await ConnectAsync();
		var directoryId = _env.Directory.NodeId;

		var initial = await client.LocalToolsGetAsync(directoryId);
		var stored  = await client.LocalToolsSetAsync(directoryId, enabled: false, ["Read", "Grep"], includeCmxMcp: true, []);
		var again   = await client.LocalToolsGetAsync(directoryId);
		var refused = await Assert.ThrowsAsync<TessynRpcException>(() => client.LocalToolsSetAsync(directoryId, true, ["Rm"], false, []));

		Assert.Equal(Constants.LocalTools.DefaultNativeTools, initial.NativeTools);
		Assert.Equal((false, true), (stored.Enabled, stored.IncludeCmxMcp));
		Assert.Equal(["Read", "Grep"], again.NativeTools);
		Assert.Equal(Constants.LocalTools.Catalog.Length, again.AvailableNativeTools.Count);
		Assert.Equal(-32602, refused.Code);
	}
}
