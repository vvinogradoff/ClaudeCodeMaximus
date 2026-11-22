using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>Returns a configurable activity for every session.</summary>
internal sealed class FakeActivityService : ISessionActivityService
{
	public SessionActivityModel Activity { get; set; } = new(null, null, null, null, false);

	public SessionActivityModel GetActivity(SessionNodeModel node) => Activity;
}

/// <remarks>Created by Claude</remarks>
public sealed class SessionStateBuilderTests : IDisposable
{
	private static readonly DateTimeOffset T0 = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

	private readonly string _tempDir;
	private readonly SessionRegistry _registry;
	private readonly FakeStreamingManagerForTurnService _streaming = new();
	private readonly FakeAttentionService _attention = new();
	private readonly FakeActivityService _activity = new();
	private readonly SessionStateBuilder _sut;

	public SessionStateBuilderTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "cm_test_state_" + Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
		_registry = new SessionRegistry(
			new AppSettingsService(_tempDir),
			new FakeSessionFileService(),
			new FakeDirectoryLabelService(),
			new FakeClaudeSessionStatusService());
		_sut = new SessionStateBuilder(_registry, TestEngines.Claude(_streaming), _attention, _activity);
	}

	public void Dispose() => Directory.Delete(_tempDir, recursive: true);

	private static SessionNodeModel Node(bool favorite = false) =>
		new() { Name = "S", FileName = "s.txt", NodeId = Guid.NewGuid().ToString("N"), IsFavorite = favorite };

	[Fact]
	public void IdleSession_HasNoAttentionAndFlagsOff()
	{
		var node = Node();

		var state = _sut.Build(node);

		Assert.Equal(node.NodeId, state.Id);
		Assert.False(state.Running);
		Assert.False(state.StreamingAlive);
		Assert.Null(state.Attention);
		Assert.False(state.Favorite);
		Assert.False(state.HasDraft);
		Assert.Null(state.LastActivityUtc);
		Assert.Null(state.ProfileName);
	}

	[Fact]
	public void RunningFlag_ComesFromTheRegistry()
	{
		var node = Node();
		_registry.SetSessionRunning(node.NodeId, true);

		Assert.True(_sut.Build(node).Running);
	}

	[Fact]
	public void StreamingAlive_ComesFromTheStreamingManager()
	{
		_streaming.AliveResult = true;

		Assert.True(_sut.Build(Node()).StreamingAlive);
	}

	[Fact]
	public void Favorite_ComesFromTheNode()
	{
		Assert.True(_sut.Build(Node(favorite: true)).Favorite);
	}

	[Fact]
	public void Attention_UsesTheAllOpenCountAndTopKind_NotTheFocusBadgeValues()
	{
		_attention.Summary = new AttentionSummaryModel
		{
			OpenCount = 0, TopKind = null,
			AllOpenCount = 2, AllOpenTopKind = AttentionKind.Decided,
		};

		var attention = _sut.Build(Node()).Attention;

		Assert.NotNull(attention);
		Assert.Equal(2, attention!.Count);
		Assert.Equal(AttentionKind.Decided, attention.TopKind);
	}

	[Fact]
	public void Attention_IsOmittedWhenNothingIsOpen()
	{
		_attention.Summary = new AttentionSummaryModel { OpenCount = 0, AllOpenCount = 0 };

		Assert.Null(_sut.Build(Node()).Attention);
	}

	[Fact]
	public void ActivityFields_AreCopiedFromTheActivityService()
	{
		_activity.Activity = new SessionActivityModel(T0.AddMinutes(3), T0.AddMinutes(1), T0.AddMinutes(2), "Work", true);

		var state = _sut.Build(Node());

		Assert.Equal(T0.AddMinutes(3), state.LastActivityUtc);
		Assert.Equal(T0.AddMinutes(1), state.LastPromptUtc);
		Assert.True(state.HasDraft);
		Assert.Equal("Work", state.ProfileName);
	}

	// ── Wire shape ───────────────────────────────────────────────────────────────

	[Fact]
	public void SerializedState_UsesCamelCaseNames_OmitsNulls_AndWritesAttentionKindAsAWireName()
	{
		_attention.Summary = new AttentionSummaryModel { AllOpenCount = 3, AllOpenTopKind = AttentionKind.Critical };
		_activity.Activity = new SessionActivityModel(T0, T0, null, null, true);
		var node = Node(favorite: true);

		var json = JsonSerializer.SerializeToNode(_sut.Build(node), TdcpJsonOptions.Default)!.AsObject();

		Assert.Equal(node.NodeId, json["id"]!.GetValue<string>());
		Assert.True(json["favorite"]!.GetValue<bool>());
		Assert.True(json["hasDraft"]!.GetValue<bool>());
		Assert.Equal(3, json["attention"]!["count"]!.GetValue<int>());
		Assert.Equal("critical", json["attention"]!["topKind"]!.GetValue<string>());
		Assert.False(json.ContainsKey("profileName"));
		Assert.False(json.ContainsKey("lastPromptUtc") && json["lastPromptUtc"] == null);
	}

	[Theory]
	[InlineData(AttentionKind.Critical, "critical")]
	[InlineData(AttentionKind.Ask, "ask")]
	[InlineData(AttentionKind.Todo, "todo")]
	[InlineData(AttentionKind.Alert, "alert")]
	[InlineData(AttentionKind.Decided, "decided")]
	public void AttentionKindWire_IsTheCamelCaseEnumName(AttentionKind kind, string wire)
	{
		var json = JsonSerializer.SerializeToNode(new RowAttentionDto(1, kind), TdcpJsonOptions.Default)!;

		Assert.Equal(wire, json["topKind"]!.GetValue<string>());
		var back = JsonSerializer.Deserialize<RowAttentionDto>(json.ToJsonString(), TdcpJsonOptions.Default);
		Assert.Equal(kind, back!.TopKind);
	}

	[Fact]
	public void AttentionSummaryDto_MapsEveryFieldOfTheModel_AndOmitsNullKinds()
	{
		var model = new AttentionSummaryModel { OpenCount = 1, TopKind = AttentionKind.Ask, AllOpenCount = 4, AllOpenTopKind = AttentionKind.Critical };

		var dto = model.ToDto();
		var json = JsonSerializer.SerializeToNode(dto, TdcpJsonOptions.Default)!.AsObject();

		Assert.Equal(1, dto.OpenCount);
		Assert.Equal(AttentionKind.Ask, dto.TopKind);
		Assert.Equal(4, dto.AllOpenCount);
		Assert.Equal(AttentionKind.Critical, dto.AllOpenTopKind);
		Assert.Equal("ask", json["topKind"]!.GetValue<string>());
		Assert.Equal("critical", json["allOpenTopKind"]!.GetValue<string>());

		var empty = JsonSerializer.SerializeToNode(new AttentionSummaryModel().ToDto(), TdcpJsonOptions.Default)!.AsObject();
		Assert.False(empty.ContainsKey("topKind"));
		Assert.False(empty.ContainsKey("allOpenTopKind"));
		Assert.Equal(0, empty["openCount"]!.GetValue<int>());
	}

	[Fact]
	public void AttentionSummaryDto_RoundTripsThroughJson()
	{
		var dto = new AttentionSummaryModel { OpenCount = 2, TopKind = AttentionKind.Todo, AllOpenCount = 3, AllOpenTopKind = AttentionKind.Alert }.ToDto();

		var json = JsonSerializer.Serialize(dto, TdcpJsonOptions.Default);
		var back = JsonSerializer.Deserialize<AttentionSummaryDto>(json, TdcpJsonOptions.Default);

		Assert.Equal(dto, back);
	}
}
