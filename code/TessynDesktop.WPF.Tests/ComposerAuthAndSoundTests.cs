using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>W1b: the auth-error indicator with Re-authenticate, the Focus item sound with its acknowledgement, the client's presence registration, and the two W1a leftovers (R1, R2).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ComposerAuthAndSoundTests : IDisposable
{
	private const string Node = "a";

	private readonly FakeTessynClient _client = new();
	private readonly FakeTranscriptSurface _surface = new();
	private readonly List<IDisposable> _disposables = [];

	public void Dispose()
	{
		foreach (var d in _disposables)
			d.Dispose();
	}

	private FakeSession AddSession(string id = Node)
	{
		var session = FakeSession.WithEntries(4, "Session " + id);
		_client.Sessions[id] = session;
		return session;
	}

	private SessionPaneViewModel CreatePane()
	{
		var pane = new SessionPaneViewModel(_client, _surface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), new ImmediateUiDispatcher(),
			new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance)
		{
			LiveFlushInterval = TimeSpan.Zero,
		};
		pane.DraftSync.Delay = TimeSpan.FromMinutes(5);
		_disposables.Add(pane);
		return pane;
	}

	private static async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow.AddSeconds(10);
		while (!condition() && DateTime.UtcNow < deadline)
			await Task.Delay(10);

		Assert.True(condition(), "Not reached in time: " + what);
	}

	private static SessionStateDto State(bool authError) => new(Node, false, false, null, false, null, null, false, null, authError);

	// ---- auth error ----

	[Fact]
	public async Task ASessionWithTheAuthErrorFlag_ShowsTheIndicator_WhenOpened()
	{
		AddSession().AuthError = true;
		var pane = CreatePane();

		await pane.OpenAsync(Node);

		Assert.True(pane.HasAuthError);
	}

	[Fact]
	public async Task TheIndicator_FollowsSessionChanged_BothWays()
	{
		AddSession();
		var pane = CreatePane();
		await pane.OpenAsync(Node);
		Assert.False(pane.HasAuthError);

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State(authError: true)));
		Assert.True(pane.HasAuthError);

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State(authError: false)));
		Assert.False(pane.HasAuthError);
	}

	[Fact]
	public async Task TheIndicator_BelongsToTheOpenSession()
	{
		AddSession().AuthError = true;
		AddSession("b");
		var pane = CreatePane();
		await pane.OpenAsync(Node);

		await pane.OpenAsync("b");

		Assert.False(pane.HasAuthError);
	}

	[Fact]
	public async Task ReAuthenticate_StartsTheLoginForTheSessionsProfile()
	{
		AddSession().LastUsed = new LastUsedSettingsDto("default", "default", "work", "Work");
		_client.Sessions[Node].AuthError = true;
		var pane = CreatePane();
		await pane.OpenAsync(Node);

		await pane.ReauthenticateCommand.ExecuteAsync(null);

		Assert.Contains("profile.authenticate:work", _client.Calls);
		Assert.Equal(Constants.ComposerTexts.ReauthenticateStarted, pane.AuthNotice);
	}

	[Fact]
	public async Task ReAuthenticate_PrefersTheProfileThatWasPicked_AndFallsBackToDefault()
	{
		_client.Catalog = new([new ModelOptionDto("default", "Default", null)], ["default"], [new ProfileOptionDto("default", "[Sys] Default"), new ProfileOptionDto("pick", "Pick")]);
		AddSession().AuthError = true;
		var pane = CreatePane();
		await pane.OpenAsync(Node);
		await pane.LoadCatalogAsync();

		await pane.ReauthenticateCommand.ExecuteAsync(null);
		pane.SelectedProfile = pane.Profiles.Single(p => p.Id == "pick");
		await pane.ReauthenticateCommand.ExecuteAsync(null);

		Assert.Equal(["profile.authenticate:default", "profile.authenticate:pick"], _client.Calls.Where(c => c.StartsWith("profile.authenticate", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task ReAuthenticate_SaysWhenALoginIsAlreadyRunning_AndWhenItFails()
	{
		AddSession().AuthError = true;
		var pane = CreatePane();
		await pane.OpenAsync(Node);

		_client.AuthenticateStarted = false;
		await pane.ReauthenticateCommand.ExecuteAsync(null);
		Assert.Equal(Constants.ComposerTexts.ReauthenticateRunning, pane.AuthNotice);

		_client.ProfileException = new IOException("no such profile");
		await pane.ReauthenticateCommand.ExecuteAsync(null);
		Assert.Equal(Constants.ComposerTexts.ReauthenticateFailed + "no such profile", pane.AuthNotice);
	}

	[Fact]
	public async Task TheNotice_GoesWhenTheErrorGoes()
	{
		AddSession().AuthError = true;
		var pane = CreatePane();
		await pane.OpenAsync(Node);
		await pane.ReauthenticateCommand.ExecuteAsync(null);

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State(authError: false)));

		Assert.Null(pane.AuthNotice);
	}

	// ---- Focus item sound (FR.22) ----

	private sealed class RecordingPlayer : ISoundPlayer
	{
		public List<string> Played { get; } = [];

		public void Play(string file) => Played.Add(file);
	}

	private sealed class ManualClock : TimeProvider
	{
		private DateTimeOffset _now = new(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => _now;

		public void Advance(TimeSpan by) => _now += by;
	}

	private (FocusItemSoundService Service, RecordingPlayer Player, ManualClock Clock) CreateSound(int throttleSeconds = 3)
	{
		_client.SettingsValues[RemoteProtocol.SettingsApi.NotificationsSection] = new NotificationsSettingsDto(true, @"C:\Windows\Media\done.wav", throttleSeconds);
		var player = new RecordingPlayer();
		var clock = new ManualClock();
		var service = new FocusItemSoundService(_client, player, clock, NullLogger<FocusItemSoundService>.Instance);
		service.Attach();
		_disposables.Add(service);
		return (service, player, clock);
	}

	private void RaiseItem(ManualClock clock, string nodeId, string itemId = "A1", string kind = "ask", TimeSpan? age = null) =>
		_client.RaiseNotification(
			RemoteProtocol.Notifications.FocusItemCreatedEvent,
			new FocusItemCreatedPayload(nodeId, itemId, kind, clock.GetUtcNow() - (age ?? TimeSpan.Zero)));

	[Fact]
	public async Task ANewFocusItem_PlaysTheConfiguredSoundOnce_AndAcknowledgesItOnce()
	{
		var (service, player, clock) = CreateSound();

		RaiseItem(clock, Node);
		await service.WhenIdleAsync();

		Assert.Equal([@"C:\Windows\Media\done.wav"], player.Played);
		Assert.Equal([Node], _client.AckedSounds);
	}

	[Theory]
	[InlineData("critical")]
	[InlineData("ask")]
	[InlineData("todo")]
	[InlineData("alert")]
	[InlineData("decided")]
	public async Task EveryKindOfNewItem_Plays(string kind)
	{
		var (service, player, clock) = CreateSound();

		RaiseItem(clock, Node, kind: kind);
		await service.WhenIdleAsync();

		Assert.Single(player.Played);
	}

	[Fact]
	public async Task ATurnCompletion_PlaysNothing_AndAcknowledgesNothing()
	{
		var (service, player, _) = CreateSound();

		_client.RaiseTurnCompleted(Node, "t1");
		_client.RaiseTurnCompleted(Node, "t2", cancelled: true);
		_client.RaiseTurnCompleted(Node, "t3", error: "boom");
		await service.WhenIdleAsync();

		Assert.Empty(player.Played);
		Assert.Empty(_client.AckedSounds);
	}

	[Fact]
	public async Task WhenTheSessionTurnedTheSoundOff_NothingPlays()
	{
		var (service, player, clock) = CreateSound();
		_client.SetOptionsHandler = (_, _, _, _) => new SessionSetOptionsResult(new SessionOptionsDto(null, false, PlayCompletionSound: false), null);

		RaiseItem(clock, Node);
		await service.WhenIdleAsync();

		Assert.Empty(player.Played);
		Assert.Empty(_client.AckedSounds);
	}

	[Fact]
	public async Task TheSessionsOption_IsReadWithoutChangingAnything()
	{
		var (service, _, clock) = CreateSound();

		RaiseItem(clock, Node);
		await service.WhenIdleAsync();

		Assert.Contains($"session.setOptions:{Node}:::", _client.Calls);
	}

	[Fact]
	public async Task ABurst_IsOneSound_AcknowledgedForEveryItem_AndPlaysAgainAfterTheWindow()
	{
		var (service, player, clock) = CreateSound(throttleSeconds: 3);

		RaiseItem(clock, Node, "A1");
		await service.WhenIdleAsync();
		clock.Advance(TimeSpan.FromSeconds(1));
		RaiseItem(clock, Node, "A2");
		RaiseItem(clock, "b", "A1");
		await service.WhenIdleAsync();
		Assert.Single(player.Played);
		Assert.Equal([Node, Node, "b"], _client.AckedSounds);

		clock.Advance(TimeSpan.FromSeconds(3));
		RaiseItem(clock, Node, "A3");
		await service.WhenIdleAsync();
		Assert.Equal(2, player.Played.Count);
	}

	[Fact]
	public async Task TheThrottle_IsGlobal_NotPerSession()
	{
		var (service, player, clock) = CreateSound();

		RaiseItem(clock, "a");
		RaiseItem(clock, "b");
		await service.WhenIdleAsync();

		Assert.Single(player.Played);
	}

	[Fact]
	public async Task AStoredThrottleOfTheOldDefault_IsReadAsThreeSeconds()
	{
		var (service, player, clock) = CreateSound(throttleSeconds: 180);

		RaiseItem(clock, Node);
		await service.WhenIdleAsync();
		clock.Advance(TimeSpan.FromSeconds(4));
		RaiseItem(clock, Node, "A2");
		await service.WhenIdleAsync();

		Assert.Equal(2, player.Played.Count);
	}

	[Fact]
	public async Task AReplayedEvent_PlaysNothing()
	{
		var (service, player, clock) = CreateSound();

		RaiseItem(clock, Node, age: TimeSpan.FromSeconds(31));
		await service.WhenIdleAsync();

		Assert.Empty(player.Played);
		Assert.Empty(_client.AckedSounds);
	}

	[Fact]
	public async Task WhenTheSettingsCannotBeRead_NothingPlaysAndNothingIsAcknowledged_SoTheHostPlaysItsOwn()
	{
		var player = new RecordingPlayer();
		var clock = new ManualClock();
		_client.SettingsValues.Clear();
		using var service = new FocusItemSoundService(_client, player, clock, NullLogger<FocusItemSoundService>.Instance);
		service.Attach();

		RaiseItem(clock, Node);
		await service.WhenIdleAsync();

		Assert.Empty(player.Played);
		Assert.Empty(_client.AckedSounds);
	}

	[Fact]
	public async Task TheSettings_AreReadOnceWhileFresh()
	{
		var (service, _, clock) = CreateSound(throttleSeconds: 0);

		RaiseItem(clock, Node, "A1");
		RaiseItem(clock, Node, "A2");
		await service.WhenIdleAsync();
		Assert.Equal(1, _client.Calls.Count(c => c.StartsWith("settings.get:notifications", StringComparison.Ordinal)));

		clock.Advance(Constants.SoundRules.SettingsCacheAge + TimeSpan.FromSeconds(1));
		RaiseItem(clock, Node, "A3");
		await service.WhenIdleAsync();
		Assert.Equal(2, _client.Calls.Count(c => c.StartsWith("settings.get:notifications", StringComparison.Ordinal)));
	}

	// ---- presence registration ----

	[Fact]
	public void TheClientFactory_GivesEveryStartTheSameClientId()
	{
		using var temp = new TempAppPaths();
		var store = new JsonClientStateStore(temp.Paths, NullLogger<JsonClientStateStore>.Instance);
		var factory = new TessynClientFactory(store);
		var endpoint = new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "token", "run", 1);

		_ = factory.Create(endpoint);
		var first = store.State.ClientId;
		_ = factory.Create(endpoint);

		Assert.False(string.IsNullOrWhiteSpace(first));
		Assert.StartsWith("wpf-", first);
		Assert.True(first!.Length <= 64);
		Assert.Equal(first, store.State.ClientId);
	}

	// ---- R1: the live row stays while the answer is being appended ----

	[Fact]
	public async Task R1_ASessionChangedDuringTheHandOff_DoesNotClearTheLiveRowBeforeTheEntryArrives()
	{
		var session = AddSession();
		var pane = CreatePane();
		await pane.OpenAsync(Node);
		_client.RaiseTurnStarted(Node, "t1");
		_client.RaiseTurnStatus(Node, "t1", TurnState.Running);
		_client.RaiseTurnDelta(Node, "t1", 1, "the answer");
		session.Entries.Add(FakeSession.MakeEntry(4));
		_surface.Calls.Clear();
		// The daemon's session.changed (the debounced flush) lands while the append page is on its way.
		_client.AfterPageHook = () => _client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State(authError: false)));

		_client.RaiseTurnCompleted(Node, "t1");
		await pane.WhenIdleAsync();

		var appended = _surface.Calls.FindIndex(c => c.StartsWith("append:", StringComparison.Ordinal));
		var cleared = _surface.Calls.FindIndex(c => c == "live:none");
		Assert.True(appended >= 0 && cleared > appended, "calls: " + string.Join(", ", _surface.Calls));
	}

	// ---- R2: a pick that left the catalog is not sent ----

	[Fact]
	public async Task R2_APickWhoseIdLeftTheCatalog_IsNotSent()
	{
		_client.Catalog = new([new ModelOptionDto("default", "Default", null)], ["default"], [new ProfileOptionDto("default", "[Sys] Default"), new ProfileOptionDto("work", "Work")]);
		AddSession();
		var pane = CreatePane();
		await pane.OpenAsync(Node);
		await pane.LoadCatalogAsync();
		pane.SelectedProfile = pane.Profiles.Single(p => p.Id == "work");

		_client.Catalog = new([new ModelOptionDto("default", "Default", null)], ["default"], [new ProfileOptionDto("default", "[Sys] Default")]);   // "work" was deleted
		await pane.LoadCatalogAsync();
		pane.InputText = "hello";
		await pane.SendCommand.ExecuteAsync(null);

		Assert.Equal("default", pane.SelectedProfile!.Id);
		Assert.Null(_client.TurnSends.Single().ProfileId);
	}

	[Fact]
	public async Task R2_APickThatIsStillListed_IsStillSent()
	{
		_client.Catalog = new([new ModelOptionDto("default", "Default", null)], ["default"], [new ProfileOptionDto("default", "[Sys] Default"), new ProfileOptionDto("work", "Work")]);
		AddSession();
		var pane = CreatePane();
		await pane.OpenAsync(Node);
		await pane.LoadCatalogAsync();
		pane.SelectedProfile = pane.Profiles.Single(p => p.Id == "work");

		pane.InputText = "hello";
		await pane.SendCommand.ExecuteAsync(null);

		Assert.Equal("work", _client.TurnSends.Single().ProfileId);
	}
}
