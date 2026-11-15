using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>
/// ISSUE.013 R5 on the wire (W5 review H1): <c>SessionStateDto</c> carries the pinned profile and a queued switch, taken from the same source as the
/// <c>profile=</c> and <c>switching-to=</c> of <c>list_sessions</c>; every pin change publishes the session state. Additive, golden-shape.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionStatePinTests : IDisposable
{
	private readonly ProfileSwitchEnv _env = new();
	private readonly SessionStateBuilder _states;
	private readonly List<string> _changed = [];

	public SessionStatePinTests()
	{
		_env.AddProfile("p1", "Account A");
		_env.AddProfile("p2", "Account B");
		_states = new SessionStateBuilder(_env.Registry, _env.Streaming, new FakeAttentionService(), new FakeActivityService(), null, null, _env.Resolver);
		_env.Registry.SessionSettingsChanged += (_, e) => _changed.Add(e.NodeId);
	}

	public void Dispose() => _env.Dispose();

	[Fact]
	public void TheWireShape_AddsThreeOptionalFields_OmittedWhenNull_AndAnOlderDaemonMeansNoPin()
	{
		var pinned = new SessionStateDto("n", false, false, null, false, null, null, false, "Old", PinnedProfileId: "p2", PinnedProfileName: "Account B", SwitchingToProfileName: "Account A");
		var json = JsonSerializer.SerializeToNode(pinned, TdcpJsonOptions.Default)!.AsObject();
		Assert.Equal("p2", json["pinnedProfileId"]!.GetValue<string>());
		Assert.Equal("Account B", json["pinnedProfileName"]!.GetValue<string>());
		Assert.Equal("Account A", json["switchingToProfileName"]!.GetValue<string>());

		var plain = JsonSerializer.SerializeToNode(new SessionStateDto("n", false, false, null, false, null, null, false, null), TdcpJsonOptions.Default)!.AsObject();
		Assert.False(plain.ContainsKey("pinnedProfileId"));
		Assert.False(plain.ContainsKey("pinnedProfileName"));
		Assert.False(plain.ContainsKey("switchingToProfileName"));

		const string older = """{"id":"n","running":false,"streamingAlive":false,"favorite":false,"hasDraft":false,"profileName":"X"}""";
		var read = JsonSerializer.Deserialize<SessionStateDto>(older, TdcpJsonOptions.Default)!;
		Assert.Null(read.PinnedProfileId);
		Assert.Null(read.PinnedProfileName);
		Assert.Null(read.SwitchingToProfileName);
	}

	[Fact]
	public void TheState_FollowsThePin_NotTheLastUserEntry_AndMatchesTheListSessionsLabel()
	{
		var node = _env.AddSession("S", profileId: "p1");
		var before = _states.Build(node);
		Assert.Equal("p1", before.PinnedProfileId);
		Assert.Equal("Account A", before.PinnedProfileName);
		Assert.Null(before.SwitchingToProfileName);

		_env.WriteSession(_env.ProfilesRoot + "\\p1", node);
		_env.Switch.Switch(node.NodeId, "p2", includeDescendants: false);

		var after = _states.Build(node);
		Assert.Equal("p2", after.PinnedProfileId);
		Assert.Equal("Account B", after.PinnedProfileName);
		Assert.Equal(_env.Registry.SnapshotSessionsForListing().Single(s => s.NodeId == node.NodeId).ProfileLabel, after.PinnedProfileName);
	}

	[Fact]
	public void AQueuedSwitch_IsShownAsSwitchingTo_UntilTheTurnEnds_ThenBecomesThePin()
	{
		var node = _env.AddSession("S", profileId: "p1");
		_env.WriteSession(_env.ProfilesRoot + "\\p1", node);
		_env.Registry.SetSessionRunning(node.NodeId, true);

		_env.Switch.Switch(node.NodeId, "p2", includeDescendants: false);
		var queued = _states.Build(node);
		Assert.Equal("Account A", queued.PinnedProfileName);
		Assert.Equal("Account B", queued.SwitchingToProfileName);

		_env.Registry.SetSessionRunning(node.NodeId, false);
		_changed.Clear();
		Assert.True(_env.Switch.ApplyPending(node));

		var done = _states.Build(node);
		Assert.Equal("Account B", done.PinnedProfileName);
		Assert.Null(done.SwitchingToProfileName);
		Assert.Contains(node.NodeId, _changed);   // open clients are told when the queued switch completes
	}

	[Fact]
	public void ASessionWithoutAPin_HasNoPinFields_AndTheDefaultPinIsLabelledDefault()
	{
		var plain = _env.AddSession("Plain");
		var state = _states.Build(plain);
		Assert.Null(state.PinnedProfileId);
		Assert.Null(state.PinnedProfileName);

		var onDefault = _env.AddSession("D");
		_env.WriteSession(_env.DefaultHome, onDefault);
		onDefault.ProfileId = "default";
		Assert.Equal("default", _states.Build(onDefault).PinnedProfileId);
		Assert.Equal(Constants.ProfileSwitch.DefaultProfileName, _states.Build(onDefault).PinnedProfileName);
	}

	[Fact]
	public void AMissingPinnedProfile_CarriesTheMissingSuffix()
	{
		var node = _env.AddSession("S", profileId: "p1");
		_env.Settings.Settings.Profiles.RemoveAll(p => p.ProfileId == "p1");

		Assert.EndsWith(Constants.ProfileSwitch.MissingProfileSuffix, _states.Build(node).PinnedProfileName);
	}

	[Fact]
	public void ARecordedTurnProfile_ThatChangesThePin_PublishesTheState()
	{
		var node = _env.AddSession("S");   // not pinned yet
		_changed.Clear();

		_env.Switch.RecordTurnProfile(node, "Account A");

		Assert.Equal("p1", node.ProfileId);
		Assert.Contains(node.NodeId, _changed);
	}

	[Fact]
	public void ADroppedQueuedSwitch_PublishesTheState_SoTheSwitchingToMarkGoes()
	{
		var node = _env.AddSession("S", profileId: "p1");
		node.PendingProfileId = "gone";
		_changed.Clear();

		Assert.False(_env.Switch.ApplyPending(node));

		Assert.Null(node.PendingProfileId);
		Assert.Contains(node.NodeId, _changed);
	}
}
