using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;
using Xunit;

namespace TessynDesktop.Tests.Contract;

/// <summary>
/// The J1 methods on the wire (P1.J1): golden result shapes over a real loopback server, the error codes and refusal messages, the capability
/// flags of <c>client.hello</c>, and the guarantee that every new method is loopback only (the relay answers -32601). Set
/// <c>TESSYN_UPDATE_GOLDEN=1</c> to rewrite the golden files, then review the diff.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeMutationWireTests : IDisposable
{
	/// <summary>Every method the J1 package adds; none may be reachable over the relay.</summary>
	public static readonly string[] J1Methods =
	[
		"tree.addDirectory", "tree.addGroup", "tree.renameNode", "tree.deleteNode", "tree.moveNode", "tree.moveTargets",
		"tree.nodeInfo", "tree.refreshGitOrigins", "session.setOptions", "session.resetConversation", "localTools.get", "localTools.set",
	];

	private readonly TreeMutationWireEnv _env = new();

	public void Dispose() => _env.Dispose();

	private static JsonObject Args(params (string Key, JsonNode? Value)[] values)
	{
		var result = new JsonObject();
		foreach (var (key, value) in values)
			result[key] = value;
		return result;
	}

	private async Task<JsonNode> ResultAsync(string method, JsonNode? @params)
	{
		var response = await _env.CallAsync(method, @params);
		Assert.Null(response["error"]);
		return response["result"]!;
	}

	// ── golden shapes ────────────────────────────────────────────────────────

	[Fact]
	public async Task TreeAddDirectory_ResultShape()
	{
		var path = Path.Combine(_env.Root, "added");
		Directory.CreateDirectory(path);

		AssertGolden("tree.addDirectory", await ResultAsync("tree.addDirectory", Args(("path", path))));
	}

	[Fact]
	public async Task TreeAddGroup_ResultShape() =>
		AssertGolden("tree.addGroup", await ResultAsync("tree.addGroup", Args(("parentId", _env.Directory.NodeId), ("name", "Group"))));

	[Fact]
	public async Task TreeRenameNode_ResultShape() =>
		AssertGolden("tree.renameNode", await ResultAsync("tree.renameNode", Args(("nodeId", _env.Session.NodeId), ("name", "Renamed"))));

	[Fact]
	public async Task TreeDeleteNode_ResultShape_Blocked_AndDeleted()
	{
		var blocked = await ResultAsync("tree.deleteNode", Args(("nodeId", _env.Directory.NodeId)));
		var deleted = await ResultAsync("tree.deleteNode", Args(("nodeId", _env.AddDirectory("empty").NodeId)));

		AssertGolden("tree.deleteNode.blocked", blocked);
		AssertGolden("tree.deleteNode.deleted", deleted);
	}

	[Fact]
	public async Task TreeMoveNode_ResultShape()
	{
		var target = _env.AddDirectory("target");

		AssertGolden("tree.moveNode", await ResultAsync("tree.moveNode", Args(("nodeId", _env.Session.NodeId), ("parentId", target.NodeId), ("index", 0))));
	}

	[Fact]
	public async Task TreeMoveTargets_ResultShape()
	{
		var group = _env.Turn.Registry.AddGroup(_env.Directory, "Group");
		_env.AddDirectory("other");
		_env.Origins.Origins[_env.Directory.Path] = "git@github.com:org/repo.git";

		var result = await ResultAsync("tree.moveTargets", Args(("nodeId", _env.Session.NodeId)));

		Assert.Equal([_env.Directory.NodeId, group.NodeId], result["parentIds"]!.AsArray().Select(n => n!.GetValue<string>()));
		AssertGolden("tree.moveTargets", result);
	}

	[Fact]
	public async Task TreeNodeInfo_ResultShape()
	{
		_env.Origins.Origins[_env.Directory.Path] = "git@github.com:org/repo.git";

		AssertGolden("tree.nodeInfo", await ResultAsync("tree.nodeInfo", Args(("nodeIds", new JsonArray(_env.Session.NodeId, _env.Directory.NodeId, "unknown")))));
	}

	[Fact]
	public async Task TreeRefreshGitOrigins_ResultShape() =>
		AssertGolden("tree.refreshGitOrigins", await ResultAsync("tree.refreshGitOrigins", Args()));

	[Fact]
	public async Task SessionSetOptions_ResultShape_WithAndWithoutCorrections()
	{
		var plain = await ResultAsync("session.setOptions", Args(("nodeId", _env.Session.NodeId), ("autoCommit", true), ("autoDocument", true), ("playCompletionSound", false)));
		var noPreference = await ResultAsync("session.setOptions", Args(("nodeId", _env.Session.NodeId), ("autoCommit", null)));

		AssertGolden("session.setOptions", plain);
		AssertGolden("session.setOptions.noPreference", noPreference);
	}

	[Fact]
	public async Task SessionResetConversation_ResultShape()
	{
		_env.Session.ClaudeSessionId = "claude-1";

		AssertGolden("session.resetConversation", await ResultAsync("session.resetConversation", Args(("nodeId", _env.Session.NodeId))));
	}

	[Fact]
	public async Task LocalToolsGetAndSet_ResultShape()
	{
		_env.Turn.Settings.Settings.ExternalMcpServers.Add(new ExternalMcpServerModel { Id = "mcp-1", DisplayName = "Linear" });
		var directoryId = _env.Directory.NodeId;

		var get = await ResultAsync("localTools.get", Args(("directoryId", directoryId)));
		var set = await ResultAsync("localTools.set", Args(
			("directoryId", directoryId), ("enabled", false), ("nativeTools", new JsonArray("Read", "Bash")), ("includeCmxMcp", true), ("includedMcpServerIds", new JsonArray("mcp-1"))));

		AssertGolden("localTools.get", get);
		AssertGolden("localTools.set", set);
	}

	// ── tri-state autoCommit on the wire ─────────────────────────────────────

	[Fact]
	public async Task SessionSetOptions_AnAbsentAutoCommitKeyIsLeftAlone_AnExplicitNullIsNoPreference()
	{
		_env.Session.IsAutoCommit = true;

		await ResultAsync("session.setOptions", Args(("nodeId", _env.Session.NodeId), ("autoDocument", true)));
		Assert.True(_env.Session.IsAutoCommit);

		await ResultAsync("session.setOptions", Args(("nodeId", _env.Session.NodeId), ("autoCommit", null)));
		Assert.Null(_env.Session.IsAutoCommit);
	}

	// ── errors ───────────────────────────────────────────────────────────────

	[Theory]
	[InlineData("tree.addDirectory", "{}", -32602)]
	[InlineData("tree.addGroup", """{"parentId":"x"}""", -32602)]
	[InlineData("tree.renameNode", """{"nodeId":"x","name":5}""", -32602)]
	[InlineData("tree.deleteNode", """{"nodeId":"x","force":"yes"}""", -32602)]
	[InlineData("tree.moveNode", """{"nodeId":"x","parentId":"y","index":"1"}""", -32602)]
	[InlineData("tree.nodeInfo", """{"nodeIds":"x"}""", -32602)]
	[InlineData("tree.nodeInfo", """{"nodeIds":[1]}""", -32602)]
	[InlineData("session.setOptions", """{"nodeId":"x","autoCommit":"yes"}""", -32602)]
	[InlineData("session.setOptions", """{"nodeId":"x","playCompletionSound":1}""", -32602)]
	[InlineData("localTools.set", """{"directoryId":"x","enabled":true,"nativeTools":["Read"],"includeCmxMcp":true}""", -32602)]
	[InlineData("tree.deleteNode", """{"nodeId":"unknown"}""", -32000)]
	[InlineData("tree.moveNode", """{"nodeId":"unknown","parentId":"y"}""", -32000)]
	[InlineData("session.setOptions", """{"nodeId":"unknown"}""", -32000)]
	[InlineData("session.resetConversation", """{"nodeId":"unknown"}""", -32000)]
	[InlineData("localTools.get", """{"directoryId":"unknown"}""", -32000)]
	public async Task BadParamsAre32602_AndUnknownNodesAre32000(string method, string paramsJson, int code)
	{
		var response = await _env.CallAsync(method, JsonNode.Parse(paramsJson));

		Assert.Equal(code, response["error"]!["code"]!.GetValue<int>());
	}

	[Fact]
	public async Task ARefusedMove_CarriesTheReasonInTheMessage_AndNoPath()
	{
		var other = _env.AddDirectory("other");
		_env.Origins.Origins[_env.Directory.Path] = "git@github.com:org/repo.git";

		var response = await _env.CallAsync("tree.moveNode", Args(("nodeId", _env.Session.NodeId), ("parentId", other.NodeId)));

		Assert.Equal(-32000, response["error"]!["code"]!.GetValue<int>());
		var message = response["error"]!["message"]!.GetValue<string>();
		Assert.Equal(Constants.TreeMutation.GitOriginRefused, message);
		Assert.DoesNotContain(_env.Root, message);
		Assert.Equal(_env.Directory.NodeId, _env.Turn.Registry.Directories.Single(d => d.Sessions.Contains(_env.Session)).NodeId);
	}

	[Fact]
	public async Task AnUnknownFolder_IsRefusedWithoutThePath()
	{
		var path = Path.Combine(_env.Root, "nowhere");

		var response = await _env.CallAsync("tree.addDirectory", Args(("path", path)));

		Assert.Equal(-32000, response["error"]!["code"]!.GetValue<int>());
		Assert.DoesNotContain("nowhere", response["error"]!["message"]!.GetValue<string>());
	}

	// ── loopback only ────────────────────────────────────────────────────────

	[Theory]
	[MemberData(nameof(Methods))]
	public async Task EveryJ1Method_IsLoopbackOnly_TheRelayAnswersMethodNotFound(string method)
	{
		var response = await _env.CallAsRelayAsync(method, Args(("nodeId", _env.Session.NodeId)));

		Assert.Equal(-32601, response["error"]!["code"]!.GetValue<int>());
	}

	[Theory]
	[MemberData(nameof(Methods))]
	public async Task EveryJ1Method_IsReachableOverTheLoopbackTransport(string method)
	{
		var response = await _env.CallAsync(method, new JsonObject());

		Assert.NotEqual(-32601, response["error"]?["code"]?.GetValue<int>());
	}

	public static TheoryData<string> Methods() => [.. J1Methods];

	[Fact]
	public void TheJ1MethodNames_AreNotPartOfTheRemoteMethodContract()
	{
		var contract = typeof(Constants.Remote.Methods).GetFields().Select(f => (string)f.GetRawConstantValue()!).ToHashSet();

		Assert.Empty(J1Methods.Where(contract.Contains));
	}

	// ── capabilities ─────────────────────────────────────────────────────────

	[Fact]
	public async Task Hello_AnnouncesTheJ1Capabilities_WhenTheServicesAreWired()
	{
		var result = await ResultAsync("client.hello", new JsonObject());

		var capabilities = result["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
		Assert.Equal(["events.local", "events.resume", "tree.mutate", "tree.info", "session.options", "localtools"], capabilities);
	}

	[Fact]
	public async Task Hello_AnnouncesNoJ1Capability_WithoutTheServices()
	{
		using var bare = new TreeMutationWireEnv(withServices: false);

		var response = await bare.CallAsync("client.hello", new JsonObject());

		var capabilities = response["result"]!["capabilities"]!.AsArray().Select(c => c!.GetValue<string>()).ToArray();
		Assert.DoesNotContain("tree.mutate", capabilities);
		Assert.DoesNotContain("session.options", capabilities);
		Assert.DoesNotContain("localtools", capabilities);
	}

	// ── golden helpers ───────────────────────────────────────────────────────

	private void AssertGolden(string name, JsonNode result)
	{
		var actual = Normalize(result.ToJsonString());
		var path   = GoldenPath(name);

		if (Environment.GetEnvironmentVariable("TESSYN_UPDATE_GOLDEN") == "1")
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, actual + "\n");
		}

		Assert.True(File.Exists(path), $"Missing golden file {path}; run once with TESSYN_UPDATE_GOLDEN=1");
		Assert.Equal(File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd(), actual.Replace("\r\n", "\n").TrimEnd());
	}

	/// <summary>Replaces GUIDs (numbered by first appearance) and the temp root so the snapshot is stable across runs.</summary>
	private string Normalize(string json)
	{
		// The root first: its folder name carries a GUID, which must not be numbered like a node id.
		json = json.Replace(JsonSerializer.Serialize(_env.Root)[1..^1], "<root>");
		json = Regex.Replace(json, "<root>[^\"]*", m => m.Value.Replace("\\\\", "/"));
		var ids = new System.Collections.Generic.Dictionary<string, string>();
		json = Regex.Replace(json, "[0-9a-f]{32}", m =>
		{
			if (!ids.TryGetValue(m.Value, out var placeholder))
				ids[m.Value] = placeholder = "<id" + (ids.Count + 1) + ">";
			return placeholder;
		});
		return JsonNode.Parse(json)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
	}

	private static string GoldenPath(string name, [CallerFilePath] string thisFile = "") =>
		Path.Combine(Path.GetDirectoryName(thisFile)!, "Golden", name + ".json");
}
