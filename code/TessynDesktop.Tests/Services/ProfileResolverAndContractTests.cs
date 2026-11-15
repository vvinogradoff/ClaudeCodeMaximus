using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// ISSUE.013: pins by ProfileId and their migration on load, the resolver's no-silent-Default rule on every turn path (headless
/// turn, TurnSettingsResolver, compaction), and the loopback-only <c>profile.switch</c> TDCP method (contract tests).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileResolverAndContractTests : IDisposable
{
	private readonly ProfileSwitchEnv _env = new();
	private readonly string _p1;

	public ProfileResolverAndContractTests()
	{
		_p1 = _env.AddProfile("p1", "Account A");
		_env.AddProfile("p2", "Account B");
	}

	public void Dispose() => _env.Dispose();

	// ── Resolver ─────────────────────────────────────────────────────────────────────────────

	[Fact]
	public void Resolve_ProfileIdPin_WinsOverANameThatNoLongerMatches()
	{
		var node = _env.AddSession("N", profileId: "p1");
		node.ProfileName = "Old display name";

		var r = _env.Resolver.Resolve(node, node.ProfileName);

		Assert.True(r.IsResolved);
		Assert.Equal("p1", r.ProfileId);
		Assert.Equal(_p1, r.ConfigDir);
	}

	[Fact]
	public void Resolve_PinnedProfileThatDoesNotResolve_IsAnError_ForIdAndForNamePins()
	{
		var byId = _env.AddSession("ById", profileId: "p9");
		byId.ProfileName = "Gone";
		var byName = _env.AddSession("ByName");
		byName.ProfileName = "Also gone";

		Assert.False(_env.Resolver.Resolve(byId, byId.ProfileName).IsResolved);
		var error = _env.Resolver.Resolve(byName, byName.ProfileName).Error;
		Assert.Contains("'Also gone'", error);
		Assert.Contains("does not fall back to the Default profile", error);
	}

	[Fact]
	public void Resolve_UnpinnedSession_KeepsTodaysBehaviour_AHistoryNameThatMatchesNothingMeansDefault()
	{
		var node = _env.AddSession("N");

		Assert.True(_env.Resolver.Resolve(node, null).IsDefault);
		var fromHistory = _env.Resolver.Resolve(node, "Name only in the history");
		Assert.True(fromHistory.IsResolved);
		Assert.True(fromHistory.IsDefault);
		Assert.False(fromHistory.IsPinned);
		Assert.Equal(_p1, _env.Resolver.Resolve(node, "account a").ConfigDir); // case-insensitive, as before
	}

	[Fact]
	public void Resolve_PerTurnChoice_WinsOverThePin_AndAnUnknownChoiceIsAnError()
	{
		var node = _env.AddSession("N", profileId: "p1");

		Assert.Equal("p2", _env.Resolver.Resolve(node, node.ProfileName, "Account B").ProfileId);
		Assert.True(_env.Resolver.Resolve(node, node.ProfileName, "[Sys] me@example.com").IsDefault);
		Assert.False(_env.Resolver.Resolve(node, node.ProfileName, "Nonexistent").IsResolved);
	}

	[Fact]
	public void Resolve_CheckLogin_FlagsAStoredProfileWithoutCredentials_ButNeverTheDefaultProfile()
	{
		_env.AddProfile("p3", "Logged out", loggedIn: false);
		var node = _env.AddSession("N", profileId: "p3");
		var onDefault = _env.AddSession("D", profileId: "default");

		Assert.True(_env.Resolver.Resolve(node, null, checkLogin: true).IsLoggedOut);
		Assert.False(_env.Resolver.Resolve(node, null).IsLoggedOut);                  // not asked for: no file check
		Assert.False(_env.Resolver.Resolve(onDefault, null, checkLogin: true).IsLoggedOut);
	}

	// ── Pin migration on load ────────────────────────────────────────────────────────────────

	[Fact]
	public void PinMigration_ConvertsDisplayNamePinsToProfileIds_InGroupsToo_AndLeavesTheRestAsTheyAre()
	{
		var matched = _env.AddSession("Matched");
		matched.ProfileName = "ACCOUNT A";                         // case-insensitive match
		var unmatched = _env.AddSession("Unmatched");
		unmatched.ProfileName = "Gone";
		var sys = _env.AddSession("Sys");
		sys.ProfileName = "[Sys] me@example.com";
		var already = _env.AddSession("Already", profileId: "p2");
		var group = _env.Registry.AddGroup(_env.Dir, "G");
		var inGroup = _env.Registry.AddSessionToGroup(group, "InGroup");
		inGroup.ProfileName = "Account B";

		var migrated = new ProfilePinMigration().Migrate(_env.Settings.Settings);

		Assert.Equal(2, migrated);
		Assert.Equal("p1", matched.ProfileId);
		Assert.Equal("p2", inGroup.ProfileId);
		Assert.Null(unmatched.ProfileId);
		Assert.Equal("Gone", unmatched.ProfileName);               // left as it is (and logged)
		Assert.Null(sys.ProfileId);
		Assert.Equal("p2", already.ProfileId);
	}

	[Fact]
	public void PinMigration_RunsWhenTheSettingsLoad_AndTheMigratedPinIsPersisted()
	{
		var node = _env.AddSession("N");
		node.ProfileName = "Account B";
		_env.Settings.Save();

		var reloaded = new AppSettingsService(_env.Root);
		reloaded.Load();
		var loaded = reloaded.Settings.Tree.SelectMany(d => d.Sessions).Single(s => s.Name == "N");

		Assert.Equal("p2", loaded.ProfileId);
		reloaded.Save();
		var again = new AppSettingsService(_env.Root);
		again.Load();
		Assert.Equal("p2", again.Settings.Tree.SelectMany(d => d.Sessions).Single(s => s.Name == "N").ProfileId);
	}

	// ── Every turn path: no silent Default ───────────────────────────────────────────────────

	[Fact]
	public async Task TurnSettingsResolver_APinnedProfileThatDoesNotResolve_IsAnError()
	{
		var node = _env.AddSession("N", profileId: "p9");
		node.ProfileName = "Gone";
		var resolver = new TurnSettingsResolver(_env.Settings, _env.Turns, _env.ProfileService, new FakeModelServiceForTurnService(),
			new ProfileLabelService(_env.ProfileService, _env.Settings), _env.Resolver);

		var ex = await Assert.ThrowsAsync<TdcpException>(() => resolver.ResolveAsync(node, null));

		Assert.Contains("'Gone'", ex.Message);
	}

	[Fact]
	public async Task TurnSettingsResolver_AppliesAQueuedSwitchFirst()
	{
		var node = _env.AddSession("N", profileId: "p1");
		_env.WriteSession(_p1, node);
		node.PendingProfileId = "p2";
		var resolver = new TurnSettingsResolver(_env.Settings, _env.Turns, _env.ProfileService, new FakeModelServiceForTurnService(),
			new ProfileLabelService(_env.ProfileService, _env.Settings), _env.Resolver, _env.Switch);

		var settings = await resolver.ResolveAsync(node, null);

		Assert.Equal("Account B", settings.ProfileDisplayName);
		Assert.Equal("p2", node.ProfileId);
		Assert.True(File.Exists(_env.TranscriptIn(Path.Combine(_env.ProfilesRoot, "p2"), node)));
	}

	// ── profile.switch (TDCP, loopback only) ─────────────────────────────────────────────────

	private async Task<JsonNode> Rpc(string paramsJson, TdcpTransport transport)
	{
		var dispatcher = new TdcpDispatcher([new ProfileRpcModule(_env.Switch)]);
		var request = $$"""{"jsonrpc":"2.0","id":1,"method":"profile.switch","params":{{paramsJson}}}""";
		return (await dispatcher.HandleTextAsync(request, transport, CancellationToken.None))!;
	}

	[Fact]
	public void ProfileSwitch_IsRegisteredLoopbackOnly_UnderItsProtocolConstant()
	{
		var registry = new TdcpMethodRegistry();
		new ProfileRpcModule(_env.Switch).Register(registry);

		var entry = Assert.Single(registry.Entries);
		Assert.Equal("profile.switch", entry.Name);
		Assert.Equal(RemoteProtocol.LoopbackMethods.ProfileSwitch, entry.Name);
		Assert.False(entry.RemoteCallable);
	}

	[Fact]
	public async Task ProfileSwitch_IsNotCallableOnTheRelay()
	{
		var response = await Rpc("""{"nodeId":"x","profileId":"p1"}""", TdcpTransport.Relay);

		Assert.Equal(RemoteProtocol.ErrorCodes.MethodNotFound, response["error"]!["code"]!.GetValue<int>());
	}

	[Fact]
	public async Task ProfileSwitch_OnLoopback_SwitchesTheNodeAndItsDescendants_WithTheDocumentedResultShape()
	{
		var o = _env.AddSession("O", profileId: "p1");
		var c1 = _env.AddSession("C1", o, "p1");
		var running = _env.AddSession("Running", o, "p1");
		_env.WriteSession(_p1, o);
		_env.WriteSession(_p1, c1);
		_env.Registry.SetSessionRunning(running.NodeId, true);

		var response = await Rpc($$"""{"nodeId":"{{o.NodeId}}","profileId":"p2","includeDescendants":true}""", TdcpTransport.Loopback);
		var result = response["result"]!;

		Assert.Null(response["error"]);
		Assert.Equal(["profileId", "profileName", "nodeMoved", "moved", "queued", "oldConfigPaths"], result.AsObject().Select(p => p.Key));
		Assert.Equal("p2", result["profileId"]!.GetValue<string>());
		Assert.Equal("Account B", result["profileName"]!.GetValue<string>());
		Assert.True(result["nodeMoved"]!.GetValue<bool>());
		Assert.Equal([c1.NodeId], result["moved"]!.AsArray().Select(m => m!["nodeId"]!.GetValue<string>()));
		Assert.Equal(["nodeId", "name", "fromProfileId", "fromProfileName"], result["moved"]![0]!.AsObject().Select(p => p.Key));
		Assert.Equal([running.NodeId], result["queued"]!.AsArray().Select(m => m!["nodeId"]!.GetValue<string>()));
		Assert.Empty(result["oldConfigPaths"]!.AsArray());
	}

	[Fact]
	public async Task ProfileSwitch_WithoutIncludeDescendants_LeavesTheChildrenAlone()
	{
		var o = _env.AddSession("O", profileId: "p1");
		var c1 = _env.AddSession("C1", o, "p1");

		var response = await Rpc($$"""{"nodeId":"{{o.NodeId}}","profileId":"p2"}""", TdcpTransport.Loopback);

		Assert.Empty(response["result"]!["moved"]!.AsArray());
		Assert.Equal("p1", c1.ProfileId);
	}

	[Theory]
	[InlineData("""{"profileId":"p1"}""")]
	[InlineData("""{"nodeId":"x"}""")]
	[InlineData("""{"nodeId":"x","profileId":"p1","includeDescendants":"yes"}""")]
	public async Task ProfileSwitch_MissingOrMistypedParams_AreInvalidParams(string paramsJson)
	{
		var response = await Rpc(paramsJson, TdcpTransport.Loopback);

		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, response["error"]!["code"]!.GetValue<int>());
	}

	[Fact]
	public async Task ProfileSwitch_UnknownSessionOrProfile_IsInvalidParams_WithAUserFacingMessage()
	{
		var o = _env.AddSession("O");

		var noSession = await Rpc("""{"nodeId":"nope","profileId":"p1"}""", TdcpTransport.Loopback);
		var noProfile = await Rpc($$"""{"nodeId":"{{o.NodeId}}","profileId":"p9"}""", TdcpTransport.Loopback);

		Assert.Equal("Session not found", noSession["error"]!["message"]!.GetValue<string>());
		Assert.Contains("Profile not found", noProfile["error"]!["message"]!.GetValue<string>());
		Assert.Equal(RemoteProtocol.ErrorCodes.InvalidParams, noSession["error"]!["code"]!.GetValue<int>());
	}
}
