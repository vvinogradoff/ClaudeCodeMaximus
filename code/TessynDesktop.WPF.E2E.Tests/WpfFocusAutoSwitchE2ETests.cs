using System.Diagnostics;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for FR.6.25 (TC.DS.48, 49, 50, 53, 54; UC.DS.15): the BUILT WPF app against a real loopback daemon with three sessions, one of them with an open Focus item. The layout is driven through
/// the docking test hook (Focus tabbed over the Session view and selected, or docked beside it), the session is switched by selecting its row in the tree through UI Automation. What the app
/// did is read from the docking hook's state and the app's log (one line says why the Session view came forward).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfFocusAutoSwitchE2ETests : IDisposable
{
	private const string Session = "session";
	private const string Focus = "focus";
	private const string QuietName = "Quiet session";
	private const string BusyName = "Busy session";
	private const string ReasonText = "no open Focus items, back to the Session view";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-focus-autoswitch-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly SessionNodeModel _quiet;
	private readonly SessionNodeModel _busy;
	private readonly string _main;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationTree _rows = new();

	public WpfFocusAutoSwitchE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies("question", "answer");
		_quiet = _env.AddSession(QuietName, "quiet question", "quiet answer");
		_busy = _env.AddSession(BusyName, "busy question", "busy answer");
		_main = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_main}\",\"expandedNodeIds\":[\"{_env.Base.Directory.NodeId}\"]}}");
		_app = new AppProcessHarness(_root, daemonFile);
	}

	public void Dispose()
	{
		_host.Dispose();
		_env.Dispose();
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
			// WebView2 helper processes can hold the data folder for a moment; the folder is in %TEMP%.
		}
	}

	private async Task<Process> StartAsync()
	{
		var process = _app.Start(new Dictionary<string, string>(), ["--test-monitors", "1"]);
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", "Tree loaded:", $"Session {_main} opened", $"Focus: 0 open item(s) for {_main}");
		Assert.True(ready, "The app did not connect and open the session. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task PostBusyItemAsync()
	{
		var result = await _env.PostAsync(new JsonObject { ["kind"] = "ask", ["title"] = "Which way?" }, _busy);
		Assert.DoesNotContain("error", result.ToJsonString(), StringComparison.OrdinalIgnoreCase);
	}

	private async Task SwitchToAsync(Process process, string name, SessionNodeModel node, int openItems)
	{
		Assert.True(await _rows.SelectAsync(process, name, StepTimeout), $"The row '{name}' was not found. Log:\n" + _app.ReadLog());
		var loaded = await _app.WaitForLogAsync(process, StepTimeout, $"Session {node.NodeId} opened", $"Focus: {openItems} open item(s) for {node.NodeId}");
		Assert.True(loaded, $"'{name}' was not shown with its Focus items. Log:\n" + _app.ReadLog());
	}

	private static async Task<WpfDockingHookClient> TabFocusOverSessionAsync(Process process)
	{
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		Assert.True((await hook.TabAsync(Focus, Session)).Ok);
		await hook.BringForwardAsync(Focus);
		await hook.WaitStateAsync(r => r.Panels.Single(p => p.Id == Focus).Selected && !r.Panels.Single(p => p.Id == Session).Selected, "Focus tabbed over the Session view and selected");
		return hook;
	}

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	[Fact]
	public async Task TC_DS_48_53_a_session_without_open_items_brings_the_Session_view_forward_with_one_log_line()
	{
		using var process = await StartAsync();
		try
		{
			using var hook = await TabFocusOverSessionAsync(process);
			Assert.Equal(0, _app.Count(ReasonText));   // arranging the layout is not a switch

			await SwitchToAsync(process, QuietName, _quiet, 0);

			await hook.WaitStateAsync(r => r.Panels.Single(p => p.Id == Session).Selected && !r.Panels.Single(p => p.Id == Focus).Selected, "the Session view selected again");
			Assert.Equal(1, _app.Count(ReasonText));
			Assert.Contains(_quiet.NodeId, _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task TC_DS_49_54_a_session_with_open_items_leaves_Focus_selected_and_the_Session_view_never_switches_to_Focus()
	{
		await PostBusyItemAsync();
		using var process = await StartAsync();
		try
		{
			using var hook = await TabFocusOverSessionAsync(process);

			await SwitchToAsync(process, BusyName, _busy, 1);
			var state = await hook.StateAsync();
			Assert.True(state.Panels.Single(p => p.Id == Focus).Selected);
			Assert.False(state.Panels.Single(p => p.Id == Session).Selected);
			Assert.Equal(0, _app.Count(ReasonText));

			// Back to the Session view by hand; switching to a session with open items does not select Focus (TC.DS.54).
			await hook.BringForwardAsync(Session);
			await hook.WaitStateAsync(r => r.Panels.Single(p => p.Id == Session).Selected, "the Session view selected");
			await SwitchToAsync(process, QuietName, _quiet, 0);
			await SwitchToAsync(process, BusyName, _busy, 1);
			state = await hook.StateAsync();
			Assert.True(state.Panels.Single(p => p.Id == Session).Selected);
			Assert.False(state.Panels.Single(p => p.Id == Focus).Selected);
			Assert.Equal(0, _app.Count(ReasonText));   // the Session view was already selected: nothing was done
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task TC_DS_50_Focus_as_a_side_panel_changes_nothing_when_the_session_has_no_open_items()
	{
		using var process = await StartAsync();
		try
		{
			using var hook = await WpfDockingHookClient.ConnectAsync(process);
			Assert.True((await hook.DockAsync(Focus, "right")).Ok);
			await hook.BringForwardAsync(Focus);
			var before = await hook.StateAsync();

			await SwitchToAsync(process, QuietName, _quiet, 0);

			var after = await hook.StateAsync();
			Assert.Equal(before.Panels.Single(p => p.Id == Focus).Selected, after.Panels.Single(p => p.Id == Focus).Selected);
			Assert.Equal(before.Panels.Single(p => p.Id == Focus).Pane, after.Panels.Single(p => p.Id == Focus).Pane);
			Assert.Equal(0, _app.Count(ReasonText));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
