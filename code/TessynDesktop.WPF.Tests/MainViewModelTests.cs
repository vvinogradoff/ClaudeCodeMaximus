using Microsoft.Extensions.Logging.Abstractions;
using TessynDesktop.Client;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Rendering;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Tests.Fakes;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests;

/// <summary>Startup wiring: connect, build the tree and the session pane, restore and persist client state.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MainViewModelTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-tests-" + Guid.NewGuid().ToString("N"));
	private readonly FakeTessynClient _client = new();
	private readonly FakeTranscriptSurface _surface = new();
	private readonly TreeBuilder _b = new();

	public void Dispose()
	{
		if (Directory.Exists(_root))
			Directory.Delete(_root, recursive: true);
	}

	private async Task<(MainViewModel Main, JsonClientStateStore Store)> StartAsync(Action<ClientStateModel>? seedState = null)
	{
		_client.Directories =
		[
			_b.Directory("d1", "Repo", [_b.Group("g1", "G", _b.Session("s2", "In group"))], _b.Session("s1", "Top")),
			_b.Directory("d2", "Other", [], _b.Session("s3", "Third")),
		];
		foreach (var id in new[] { "s1", "s2", "s3" })
			_client.Sessions[id] = FakeSession.WithEntries(5, "Session " + id);

		var store = new JsonClientStateStore(new AppPaths(_root), NullLogger<JsonClientStateStore>.Instance, TimeSpan.FromMilliseconds(10));
		await store.LoadAsync();
		if (seedState != null)
			store.Update(seedState);

		var ui = new ImmediateUiDispatcher();
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "t", "r", 1)));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var main = new MainViewModel(
			connection,
			c => new TreeViewModel(c, ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero),
			c => new SessionPaneViewModel(c, _surface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), ui,
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance),
			store,
			NullLogger<MainViewModel>.Instance);

		await main.StartAsync(CancellationToken.None);
		await main.Tree!.WhenIdleAsync();
		return (main, store);
	}

	[Fact]
	public async Task Connecting_BuildsTheTreeAndTheSessionPane()
	{
		var (main, _) = await StartAsync();

		Assert.False(main.Connection.IsBannerVisible);
		Assert.NotNull(main.Session);
		Assert.Equal(["d1", "d2"], main.Tree!.Rows.Select(r => r.Id));
		Assert.Contains("3 sessions", main.StatusText);
	}

	[Fact]
	public async Task SelectingASession_OpensItInThePaneAndRemembersTheSelection()
	{
		var (main, store) = await StartAsync();
		await main.Tree!.ToggleExpandCommand.ExecuteAsync(main.Tree.Rows[1]);

		main.Tree.SelectedNode = main.Tree.Rows.Single(r => r.Id == "s3");
		await main.Session!.WhenIdleAsync();

		Assert.Equal("s3", main.Session.NodeId);
		Assert.Equal("s3", store.State.LastSelectedNodeId);
		Assert.Contains("session.entries:s3:before=-:after=-:limit=100", _client.Calls);
	}

	[Fact]
	public async Task SelectingADirectory_DoesNotOpenASession()
	{
		var (main, _) = await StartAsync();

		main.Tree!.SelectedNode = main.Tree.Rows[0];

		Assert.Null(main.Session!.NodeId);
	}

	[Fact]
	public async Task StartupRestoresTheExpandedNodesAndTheLastSelection()
	{
		var (main, _) = await StartAsync(s =>
		{
			s.ExpandedNodeIds = ["d1", "g1"];
			s.LastSelectedNodeId = "s2";
		});

		Assert.Equal(["d1", "g1", "s2", "s1", "d2"], main.Tree!.Rows.Select(r => r.Id));
		Assert.Equal("s2", main.Tree.SelectedNode?.Id);
		Assert.Equal("s2", main.Session!.NodeId);
	}

	[Fact]
	public async Task ExpandingANode_IsPersisted()
	{
		var (main, store) = await StartAsync();

		await main.Tree!.ToggleExpandCommand.ExecuteAsync(main.Tree.Rows[0]);
		await store.FlushAsync();

		Assert.Contains("d1", store.State.ExpandedNodeIds!);
		var reloaded = new JsonClientStateStore(new AppPaths(_root), NullLogger<JsonClientStateStore>.Instance);
		await reloaded.LoadAsync();
		Assert.Contains("d1", reloaded.State.ExpandedNodeIds!);
	}

	[Fact]
	public async Task NoDaemon_ShowsTheBannerAndNoTree()
	{
		var ui = new ImmediateUiDispatcher();
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var store = new JsonClientStateStore(new AppPaths(_root), NullLogger<JsonClientStateStore>.Instance);
		var main = new MainViewModel(connection, c => throw new InvalidOperationException(), c => throw new InvalidOperationException(), store, NullLogger<MainViewModel>.Instance);
		using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

		await main.StartAsync(cts.Token);

		Assert.Null(main.Tree);
		Assert.True(main.Connection.IsBannerVisible);
		Assert.Contains("Start TessynDesktop to connect", main.StatusText);
	}

	[Fact]
	public async Task AFailingSessionPane_DoesNotAbortTheTreeLoad_AndTheErrorIsVisible()
	{
		_client.Directories = [_b.Directory("d1", "Repo", [], _b.Session("s1", "Top"))];
		var ui = new ImmediateUiDispatcher();
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "t", "r", 1)));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var store = new JsonClientStateStore(new AppPaths(_root), NullLogger<JsonClientStateStore>.Instance);
		var main = new MainViewModel(connection, c => new TreeViewModel(c, ui, NullLogger<TreeViewModel>.Instance, TimeSpan.Zero),
			c => throw new InvalidOperationException("pane boom"), store, NullLogger<MainViewModel>.Instance);

		await main.StartAsync(CancellationToken.None);

		Assert.Equal(["d1"], main.Tree!.Rows.Select(r => r.Id));
		Assert.Null(main.Session);
		Assert.True(main.HasError);
		Assert.Contains("session pane", main.ErrorText);
		Assert.Contains("pane boom", main.ErrorText);
	}

	[Fact]
	public async Task AFailingTree_DoesNotPreventThePane_AndTheErrorIsVisible()
	{
		var ui = new ImmediateUiDispatcher();
		var locator = new ScriptedLocator();
		locator.Results.Enqueue(new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(new Uri("http://127.0.0.1:1/tdcp"), "t", "r", 1)));
		var connection = new ConnectionViewModel(locator, new FixedClientFactory(_client), ui, NullLogger<ConnectionViewModel>.Instance, TimeSpan.FromMilliseconds(5));
		var store = new JsonClientStateStore(new AppPaths(_root), NullLogger<JsonClientStateStore>.Instance);
		var main = new MainViewModel(connection, c => throw new InvalidOperationException("tree boom"),
			c => new SessionPaneViewModel(c, _surface, new MarkdigEntryRenderer(new UrlPolicy()), new FakeUrlLauncher(), ui,
				new SwitchLatencyTracker(NullLogger<SwitchLatencyTracker>.Instance), NullLogger<SessionPaneViewModel>.Instance),
			store, NullLogger<MainViewModel>.Instance);

		await main.StartAsync(CancellationToken.None);

		Assert.NotNull(main.Session);
		Assert.Null(main.Tree);
		Assert.True(main.HasError);
		Assert.Contains("tree boom", main.ErrorText);
	}

	[Fact]
	public async Task AFailingTreeLoad_IsShownNotOnlyLogged()
	{
		_client.TreeSnapshotException = new HttpRequestException("daemon went away");
		var (main, _) = await StartAsync();

		Assert.True(main.HasError);
		Assert.Contains("tree", main.ErrorText, StringComparison.OrdinalIgnoreCase);
	}
}
