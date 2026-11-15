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

/// <summary>W3 wiring: the host opens a picked session through the tree and the pane, sets favourites, and the module registers the tabs, the dropdown, the header star and the tree menu entry.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionListsModuleTests : IDisposable
{
	private sealed class MemoryState : IClientStateStore
	{
		public ClientStateModel State { get; } = new();

		public Task LoadAsync() => Task.CompletedTask;

		public void Update(Action<ClientStateModel> change) => change(State);

		public Task FlushAsync() => Task.CompletedTask;
	}

	private readonly FakeTessynClient _client = new();
	private readonly FakeTranscriptSurface _surface = new();
	private readonly TreeBuilder _b = new();
	private readonly ImmediateUiDispatcher _ui = new();
	private readonly ConnectionViewModel _connection;
	private readonly MainViewModel _main;
	private readonly SessionListsHost _host;

	public SessionListsModuleTests()
	{
		_client.Directories =
		[
			_b.Directory("d1", "Repo", [_b.Group("g1", "G", _b.Session("s2", "In group"))], _b.Session("s1", "Top")),
		];
		foreach (var id in new[] { "s1", "s2", "s3" })
			_client.Sessions[id] = FakeSession.WithEntries(3, "Session " + id);

		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "t", "r", 1)));
		_connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), _ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		_main = new MainViewModel(
			_connection,
			c => new TreeViewModel(c, _ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero),
			c => new SessionPaneViewModel(c, _surface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), _ui,
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance),
			new MemoryState(),
			NullLogger<MainViewModel>.Instance);
		_host = new SessionListsHost(_connection, _main, _ui, NullLoggerFactory.Instance);
	}

	public void Dispose()
	{
		_host.Recent.Dispose();
		_host.Tags.Dispose();
	}

	private async Task StartAsync()
	{
		await _main.StartAsync(CancellationToken.None);
		await _main.Tree!.WhenIdleAsync();
	}

	[Fact]
	public async Task ClientReady_AttachesBothListsToTheClient()
	{
		await StartAsync();

		Assert.Contains("recent.list", _client.Calls);
		Assert.Contains("tags.search::", _client.Calls);
	}

	[Fact]
	public async Task Open_OfATreeSession_SelectsItInTheTreeAndOpensItOnce()
	{
		await StartAsync();

		_host.Open("s2", "In group");
		await _main.Session!.WhenIdleAsync();

		Assert.Equal("s2", _main.Tree!.SelectedNode?.Id);
		Assert.Equal("s2", _main.Session.NodeId);
		Assert.Single(_client.Calls, c => c.StartsWith("session.get:s2", StringComparison.Ordinal));
		Assert.True(_main.Tree.Rows.Any(r => r.Id == "s2"), "the parents of the picked session are expanded");
	}

	[Fact]
	public async Task Open_OfASessionTheTreeDoesNotKnow_OpensItInThePaneDirectly()
	{
		await StartAsync();

		_host.Open("s3", "Not in the tree");
		await _main.Session!.WhenIdleAsync();

		Assert.Equal("s3", _main.Session.NodeId);
	}

	[Fact]
	public async Task Open_OfTheSelectedSessionWhilePaneShowsAnother_ReopensIt()
	{
		await StartAsync();
		_host.Open("s1", "Top");
		await _main.Session!.WhenIdleAsync();
		await _main.Session.OpenAsync("s2", "In group");   // the pane moved on (a jump), the tree still has s1 selected

		_host.Open("s1", "Top");
		await _main.Session.WhenIdleAsync();

		Assert.Equal("s1", _main.Session.NodeId);
	}

	[Fact]
	public async Task SetFavorite_ReturnsTheStoredValue_AndNullWhenTheCallFails()
	{
		await StartAsync();

		Assert.True(await _host.SetFavoriteAsync("s1", true));
		Assert.Contains("session.setFavorite:s1:True", _client.Calls);

		_client.SetFavoriteHandler = (_, _) => throw new InvalidOperationException("denied");
		Assert.Null(await _host.SetFavoriteAsync("s1", false));
	}

	[Fact]
	public async Task HeaderToggle_ShowsTheOpenSessionsFlagAndFlipsIt()
	{
		await StartAsync();
		await _main.Session!.OpenAsync("s1", "Top");
		Assert.False(_main.Session.IsFavorite);

		await _main.Session.ToggleFavoriteCommand.ExecuteAsync(null);
		Assert.True(_main.Session.IsFavorite);
		Assert.Contains("session.setFavorite:s1:True", _client.Calls);

		_client.RaiseNotification(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(new SessionStateDto("s1", false, false, null, false, null, null, false, null)));
		Assert.False(_main.Session.IsFavorite);   // another client unstarred it
	}

	[Fact]
	public async Task HeaderToggle_WhenTheCallFails_KeepsTheFlagAndShowsANotice()
	{
		await StartAsync();
		await _main.Session!.OpenAsync("s1", "Top");
		_client.SetFavoriteHandler = (_, _) => throw new InvalidOperationException("denied");

		await _main.Session.ToggleFavoriteCommand.ExecuteAsync(null);

		Assert.False(_main.Session.IsFavorite);
		Assert.Contains("denied", _main.Session.Notice);
	}

	[Fact]
	public async Task HeaderToggle_WithNoOpenSession_DoesNothing()
	{
		await StartAsync();

		await _main.Session!.ToggleFavoriteCommand.ExecuteAsync(null);

		Assert.DoesNotContain(_client.Calls, c => c.StartsWith("session.setFavorite", StringComparison.Ordinal));
	}

	[Fact]
	public void Module_RegistersTheTabsTheDropdownTheHeaderStarAndTheTreeMenuEntry()
	{
		var slots = new ShellSlots();
		var commands = new ShellCommands(new ClientSettingsStore(new MemoryState(), NullLogger<ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);

		new SessionListsModule(_host).Register(slots, commands);

		Assert.Equal([Constants.SessionLists.RecentTabTitle, Constants.SessionLists.TagsTabTitle], slots.For(Constants.Slots.LeftPanelTabs).Select(c => c.Title));
		Assert.Single(slots.For(Constants.Slots.TitleBarToolbar));
		Assert.Single(slots.For(Constants.Slots.SessionHeaderToolbar));
		var node = new TreeNodeViewModel("s1", TreeNodeKind.Session, "Top", null) { State = new SessionStateDto("s1", false, false, null, false, null, null, false, null) };
		var entry = Assert.Single(commands.BuildMenu(Constants.Menus.TreeNode, node));
		Assert.Equal(Constants.SessionLists.ToggleFavoriteCommandId, entry.CommandId);
		Assert.True(entry.IsEnabled);
	}

	[Fact]
	public async Task TreeMenuCommand_TogglesTheFavouriteOfTheRowAndOnlyAppliesToSessions()
	{
		await StartAsync();
		var slots = new ShellSlots();
		var commands = new ShellCommands(new ClientSettingsStore(new MemoryState(), NullLogger<ClientSettingsStore>.Instance), NullLogger<ShellCommands>.Instance);
		new SessionListsModule(_host).Register(slots, commands);
		var session = new TreeNodeViewModel("s1", TreeNodeKind.Session, "Top", null) { State = new SessionStateDto("s1", false, false, null, false, null, null, false, null) };
		var directory = new TreeNodeViewModel("d1", TreeNodeKind.Directory, "Repo", null);

		Assert.True(await commands.ExecuteAsync(Constants.SessionLists.ToggleFavoriteCommandId, session));
		Assert.False(await commands.ExecuteAsync(Constants.SessionLists.ToggleFavoriteCommandId, directory));

		Assert.True(session.IsFavorite);
		Assert.Contains("session.setFavorite:s1:True", _client.Calls);
		Assert.DoesNotContain("session.setFavorite:d1:True", _client.Calls);
	}
}
