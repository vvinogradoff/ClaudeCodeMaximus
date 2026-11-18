using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using Xunit;

namespace TessynDesktop.Tests.Services;

/// <summary>TC.SD.01–TC.SD.04: the Focus item sound (FR.22.1–FR.22.3) follows new items only, over a real <see cref="AttentionService"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusItemSoundNotifierTests : IDisposable
{
	private sealed class CountingSounds : ISessionSoundService
	{
		public List<string> Plays { get; } = [];
		public void PlayFocusItemSound(string nodeId) => Plays.Add(nodeId);
		public void PlayTestSound(string filePath) { }
		public IReadOnlyList<string> GetAvailableSounds() => [];
	}

	private readonly string _dir = Path.Combine(Path.GetTempPath(), "focus-sound-tests-" + Guid.NewGuid().ToString("N"));
	private readonly AttentionTestEnv _env = new();
	private readonly FakeSessionRegistry _registry = new();
	private readonly CountingSounds _sounds = new();
	private readonly ClientPresenceRegistry _presence = new();
	private readonly RemoteEventBus _bus = new();
	private readonly List<RemoteNotification> _published = [];
	private readonly AppSettingsService _settings;
	private readonly FocusItemSoundNotifier _notifier;
	private readonly SessionNodeModel _node = AttentionTestEnv.Node("n1");

	public FocusItemSoundNotifierTests()
	{
		Directory.CreateDirectory(_dir);
		_settings = new AppSettingsService(_dir);
		_registry.AddNode(_node);
		_registry.AddNode(AttentionTestEnv.Node("n2"));
		_bus.Published += n => { lock (_published) _published.Add(n); };
		_notifier = new FocusItemSoundNotifier(_env.Service, _registry, _settings, _presence, _bus, _sounds, _env.Clock);
		_notifier.Start();
	}

	public void Dispose()
	{
		_notifier.Dispose();
		_bus.Dispose();
		_env.Dispose();
		Directory.Delete(_dir, recursive: true);
	}

	private IReadOnlyList<AttentionPostOutcomeModel> Post(string title, AttentionKind kind = AttentionKind.Ask, string? key = null, SessionNodeModel? node = null) =>
		_env.Service.Post(node ?? _node, [AttentionTestEnv.Req(title, kind, key)]);

	private List<RemoteNotification> Events()
	{
		lock (_published)
			return _published.Where(n => n.Method == RemoteProtocol.Notifications.FocusItemCreatedEvent).ToList();
	}

	private void PassTheWindow() => _env.Clock.Advance(TimeSpan.FromSeconds(RemoteProtocol.Notifications.DefaultSoundThrottleSeconds + 1));

	[Theory]
	[InlineData(AttentionKind.Critical)]
	[InlineData(AttentionKind.Ask)]
	[InlineData(AttentionKind.Todo)]
	[InlineData(AttentionKind.Alert)]
	[InlineData(AttentionKind.Decided)]
	public void ANewItemOfAnyKind_RaisesTheSound(AttentionKind kind)
	{
		Post("t", kind);

		Assert.Equal(["n1"], _sounds.Plays);
	}

	[Fact]
	public void AnUpdate_RaisesNothing()
	{
		Post("t", key: "k");
		PassTheWindow();

		var outcome = Post("t again", key: "k");

		Assert.False(outcome.Single().IsCreated);
		Assert.Single(_sounds.Plays);
	}

	[Fact]
	public void AResolveAnAnswerAndADismissal_RaiseNothing()
	{
		var first  = Post("a").Single().Item;
		PassTheWindow();
		var second = Post("b").Single().Item;
		PassTheWindow();
		var third  = Post("c").Single().Item;
		Assert.Equal(3, _sounds.Plays.Count);

		PassTheWindow();
		_env.Service.ResolveBySession(_node, first.Id, AttentionStatus.Resolved, "done");
		_env.Service.MarkAnsweredByUser(_node.NodeId, [new AttentionAnswerModel { ItemId = second.Id, Answer = "yes" }]);
		_env.Service.CloseByUser(_node, third.Id);

		Assert.Equal(3, _sounds.Plays.Count);
	}

	[Fact]
	public void ATurnCompletion_RaisesNothing()
	{
		// Nothing in the daemon listens to turn completions for sound: the only trigger is the attention service.
		_env.Files.NotifyTurnCompleted(_node.FileName, cancelled: false, errorText: null);

		Assert.Empty(_sounds.Plays);
		Assert.Empty(Events());
	}

	[Fact]
	public void ABurstOfItemsInOneCall_IsOneSound()
	{
		_env.Service.Post(_node, [AttentionTestEnv.Req("a"), AttentionTestEnv.Req("b"), AttentionTestEnv.Req("c", AttentionKind.Critical)]);

		Assert.Single(_sounds.Plays);
	}

	[Fact]
	public void ItemsOfTwoSessionsInsideTheWindow_AreOneSound_AndAfterItPlayAgain()
	{
		Post("a");
		_env.Clock.Advance(TimeSpan.FromSeconds(1));
		Post("b", node: AttentionTestEnv.Node("n2"));
		Assert.Single(_sounds.Plays);

		PassTheWindow();
		Post("c");
		Assert.Equal(2, _sounds.Plays.Count);
	}

	[Fact]
	public void ASessionWithTheSwitchOff_PlaysNothingAndPublishesNothing()
	{
		_node.PlayCompletionSound = false;
		_presence.Register("wpf", [RemoteProtocol.Notifications.SoundChannel]);
		using var stream = _presence.OpenStream("wpf");

		Post("a");

		Assert.Empty(_sounds.Plays);
		Assert.Empty(Events());
	}

	[Fact]
	public void ASilentSessionDoesNotStartTheThrottleWindow()
	{
		_node.PlayCompletionSound = false;
		Post("a");

		Post("b", node: AttentionTestEnv.Node("n2"));

		Assert.Equal(["n2"], _sounds.Plays);
	}

	[Fact]
	public void WithAPresentingClient_TheEventIsPublished_AndWithoutOneItIsNot()
	{
		Post("a");
		Assert.Empty(Events());   // no client: only the host cue

		PassTheWindow();
		_presence.Register("wpf", [RemoteProtocol.Notifications.SoundChannel]);
		using var stream = _presence.OpenStream("wpf");
		var item = Post("b", AttentionKind.Alert).Single().Item;

		var published = Assert.Single(Events());
		Assert.Equal("n1", published.Params["nodeId"]!.GetValue<string>());
		Assert.Equal(item.Id, published.Params["itemId"]!.GetValue<string>());
		Assert.Equal("alert", published.Params["kind"]!.GetValue<string>());
		Assert.Contains(RemoteProtocol.Notifications.FocusItemCreatedEvent, RemoteProtocol.Notifications.LocalOnlyEvents);
	}

	[Fact]
	public void TheOldDefaultThrottle_ReadsAsTheNewOne_AndOtherValuesAreKept()
	{
		Assert.Equal(3, RemoteProtocol.Notifications.EffectiveSoundThrottleSeconds(180));
		Assert.Equal(3, RemoteProtocol.Notifications.EffectiveSoundThrottleSeconds(3));
		Assert.Equal(10, RemoteProtocol.Notifications.EffectiveSoundThrottleSeconds(10));
		Assert.Equal(0, RemoteProtocol.Notifications.EffectiveSoundThrottleSeconds(0));
		Assert.Equal(3, new AppSettingsModel().SessionCompletionSoundThrottleSeconds);
	}

	[Fact]
	public void AStoredThrottleOf180_IsAppliedAsThree()
	{
		_settings.Settings.SessionCompletionSoundThrottleSeconds = 180;
		Post("a");
		_env.Clock.Advance(TimeSpan.FromSeconds(4));

		Post("b");

		Assert.Equal(2, _sounds.Plays.Count);
	}
}
