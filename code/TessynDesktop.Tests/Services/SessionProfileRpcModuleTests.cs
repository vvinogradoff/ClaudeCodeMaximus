using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>The remote <c>session.switchProfile</c> (web client): the same switch as the loopback <c>profile.switch</c>, no local paths in the answer.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionProfileRpcModuleTests : IDisposable
{
	private readonly ProfileSwitchEnv _env = new();
	private readonly string _p1;
	private readonly string _p2;

	public SessionProfileRpcModuleTests()
	{
		_p1 = _env.AddProfile("p1", "Account A");
		_p2 = _env.AddProfile("p2", "Account B");
	}

	public void Dispose() => _env.Dispose();

	private async Task<JsonNode> Rpc(string paramsJson, TdcpTransport transport)
	{
		var dispatcher = new TdcpDispatcher([new SessionProfileRpcModule(_env.Switch)]);
		var request = $$"""{"jsonrpc":"2.0","id":1,"method":"session.switchProfile","params":{{paramsJson}}}""";
		return (await dispatcher.HandleTextAsync(request, transport, CancellationToken.None))!;
	}

	[Fact]
	public void SwitchProfile_IsRegisteredRemoteCallable_UnderItsProtocolConstant()
	{
		var registry = new TdcpMethodRegistry();
		new SessionProfileRpcModule(_env.Switch).Register(registry);

		var entry = Assert.Single(registry.Entries);
		Assert.Equal("session.switchProfile", entry.Name);
		Assert.Equal(RemoteProtocol.Methods.SessionSwitchProfile, entry.Name);
		Assert.True(entry.RemoteCallable);
	}

	[Fact]
	public async Task SwitchProfile_OnTheRelay_MovesTheSessionAndItsWorkers_AndAnswersWithoutLocalPaths()
	{
		var o = _env.AddSession("O", profileId: "p1");
		var c1 = _env.AddSession("C1", o, "p1");
		var running = _env.AddSession("Running", o, "p1");
		_env.WriteSession(_p1, o, "{\"path\":\"" + Path.Combine(_p1, "projects", "x.md").Replace("\\", "\\\\") + "\"}\n");
		_env.WriteSession(_p1, c1);
		_env.Registry.SetSessionRunning(running.NodeId, true);

		var response = await Rpc($$"""{"nodeId":"{{o.NodeId}}","profileId":"p2","includeDescendants":true}""", TdcpTransport.Relay);
		var result = response["result"]!;

		Assert.Null(response["error"]);
		Assert.Equal(["profileId", "profileName", "nodeMoved", "moved", "queued", "failed"], result.AsObject().Select(p => p.Key));
		Assert.Equal("p2", result["profileId"]!.GetValue<string>());
		Assert.Equal("Account B", result["profileName"]!.GetValue<string>());
		Assert.True(result["nodeMoved"]!.GetValue<bool>());
		Assert.Equal([c1.NodeId], result["moved"]!.AsArray().Select(m => m!["nodeId"]!.GetValue<string>()));
		Assert.Equal([running.NodeId], result["queued"]!.AsArray().Select(m => m!["nodeId"]!.GetValue<string>()));
		Assert.Empty(result["failed"]!.AsArray());
		Assert.DoesNotContain(_p1, result.ToJsonString());
		Assert.Equal("p2", o.ProfileId);
		Assert.Equal("p2", c1.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(_p2, o)));
	}

	[Fact]
	public async Task SwitchProfile_WithoutIncludeDescendants_LeavesTheWorkersAlone()
	{
		var o = _env.AddSession("O", profileId: "p1");
		var c1 = _env.AddSession("C1", o, "p1");

		var response = await Rpc($$"""{"nodeId":"{{o.NodeId}}","profileId":"p2"}""", TdcpTransport.Relay);

		Assert.Empty(response["result"]!["moved"]!.AsArray());
		Assert.Equal("p1", c1.ProfileId);
	}

	[Fact]
	public async Task SwitchProfile_ToDefault_IsAccepted()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o);

		var response = await Rpc($$"""{"nodeId":"{{o.NodeId}}","profileId":"default"}""", TdcpTransport.Relay);

		Assert.Null(response["error"]);
		Assert.Equal("default", response["result"]!["profileId"]!.GetValue<string>());
	}

	[Fact]
	public async Task SwitchProfile_AFileThatCannotBeMoved_IsReportedAsACountNotAPath()
	{
		const string localPath = @"C:\Users\someone\.claude\projects\x\held.txt";
		var stub = new FixedResultSwitchService(new ProfileSwitchResultDto(
			"p2", "Account B", NodeMoved: false, [], [], [localPath],
			[new ProfileSwitchFailureDto("n1", "Worker", [localPath, localPath + "2"])]));
		var dispatcher = new TdcpDispatcher([new SessionProfileRpcModule(stub)]);

		var response = (await dispatcher.HandleTextAsync(
			"""{"jsonrpc":"2.0","id":1,"method":"session.switchProfile","params":{"nodeId":"n1","profileId":"p2"}}""", TdcpTransport.Relay, CancellationToken.None))!;

		var failed = Assert.Single(response["result"]!["failed"]!.AsArray())!;
		Assert.Equal(["nodeId", "name", "fileCount"], failed.AsObject().Select(p => p.Key));
		Assert.Equal(2, failed["fileCount"]!.GetValue<int>());
		Assert.DoesNotContain("someone", response["result"]!.ToJsonString());
	}

	private sealed class FixedResultSwitchService(ProfileSwitchResultDto result) : IProfileSwitchService
	{
		public ProfileSwitchResultDto Switch(string nodeId, string profileId, bool includeDescendants) => result;
		public System.Collections.Generic.IReadOnlyList<TessynDesktop.Models.SessionNodeModel> GetDescendants(string nodeId) => [];
		public bool IsDescendantOf(string nodeId, string ancestorNodeId) => false;
		public bool ApplyPending(TessynDesktop.Models.SessionNodeModel node) => false;
		public void RecordTurnProfile(TessynDesktop.Models.SessionNodeModel node, string? displayName) { }
	}

	[Theory]
	[InlineData("""{"profileId":"p1"}""")]
	[InlineData("""{"nodeId":"x"}""")]
	[InlineData("""{"nodeId":"x","profileId":"p1","includeDescendants":"yes"}""")]
	public async Task SwitchProfile_MissingOrMistypedParams_AreInvalidParams(string paramsJson)
	{
		var response = await Rpc(paramsJson, TdcpTransport.Relay);

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, response["error"]!["code"]!.GetValue<int>());
	}

	[Fact]
	public async Task SwitchProfile_UnknownSessionOrProfile_IsInvalidParams_WithAUserFacingMessage()
	{
		var o = _env.AddSession("O");

		var noSession = await Rpc("""{"nodeId":"nope","profileId":"p1"}""", TdcpTransport.Relay);
		var noProfile = await Rpc($$"""{"nodeId":"{{o.NodeId}}","profileId":"p9"}""", TdcpTransport.Relay);

		Assert.Equal("Session not found", noSession["error"]!["message"]!.GetValue<string>());
		Assert.Contains("Profile not found", noProfile["error"]!["message"]!.GetValue<string>());
		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, noProfile["error"]!["code"]!.GetValue<int>());
	}

	[Fact]
	public async Task SwitchProfile_PublishesTheSessionSettingsChange_SoListsRefresh()
	{
		var o = _env.AddSession("O", profileId: "p1");
		_env.WriteSession(_p1, o);
		var raised = false;
		_env.Registry.SessionSettingsChanged += (_, e) => raised |= e.NodeId == o.NodeId;

		await Rpc($$"""{"nodeId":"{{o.NodeId}}","profileId":"p2"}""", TdcpTransport.Relay);

		Assert.True(raised);
	}
}
