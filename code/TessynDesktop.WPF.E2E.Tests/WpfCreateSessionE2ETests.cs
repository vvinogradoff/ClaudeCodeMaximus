using System.Diagnostics;
using System.Windows.Automation;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for <c>create_session</c> (FR.15.15, FR.12.35; UC.15.4, TC.15.9, TC.ME.05): the BUILT WPF app against a real loopback daemon with the real
/// tree and session modules and the real registry. The daemon creates a top-level session with the registry call <c>create_session</c> makes, right after
/// an anchor in a group, and announces it with <c>tree.changed</c> as the daemon's publisher does. The new row appears right under the anchor without moving
/// the selection, and opening it shows its own model and effort (no transcript yet) and Streaming Session Mode on.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfCreateSessionE2ETests : IDisposable
{
	private const string Opus = "claude-opus-5-5";
	private const string NewName = "Root level sessions";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-create-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly RemoteSessionEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationTree _rows = new();
	private readonly UiAutomationCommandBar _bar = new();
	private readonly GroupNodeModel _ui;
	private readonly SessionNodeModel _anchor;

	private static readonly string[] SessionRows = ["Main session", "First", "Anchor", NewName, "Last"];

	public WpfCreateSessionE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.Files.Set(_env.Session.FileName, [User("hello", 0), Assistant("hi", 1, Opus, "high")]);
		_env.Models.Models = [new ClaudeModelInfo(Opus, "opus", "Opus 5.5")];

		// Group "UI": First, Anchor, Last (the new session goes between Anchor and Last).
		_ui = _env.Registry.AddGroup(_env.Directory, "UI");
		_env.Registry.AddSessionToGroup(_ui, "First");
		_anchor = _env.Registry.AddSessionToGroup(_ui, "Anchor");
		_env.Registry.AddSessionToGroup(_ui, "Last");

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Registry, _env.Streaming, _env.Attention, activity);
		var versions = new TreeVersionTracker();
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Settings), _env.Registry, states, activity, versions,
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		var catalog = new CatalogResult(
			[new ModelOptionDto("default", "Default", null), new ModelOptionDto(Opus, "Opus 5.5", null)],
			["default", "max", "high", "medium", "low"],
			[new ProfileOptionDto("default", "[Sys] Default")]);
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), _env.Module, tree, new CatalogTestModule([], catalog)]));

		// What the daemon's notification publisher does for a session the registry added: a new tree version, announced as tree.changed.
		_env.Registry.TreeChanged += (_, e) =>
		{
			if (e.Kind == TreeChangeKind.SessionAdded)
				_host.Bus.Publish(RemoteProtocol.Events.TreeChanged, new TreeChangedPayload(versions.Increment()));
		};

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		// The main session is selected; the directory and the group are expanded.
		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_env.Session.NodeId}\",\"expandedNodeIds\":[\"{_env.Directory.NodeId}\",\"{_ui.NodeId}\"]}}");
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
		var process = _app.Start();
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", "Tree loaded:", $"Session {_env.Session.NodeId} opened");
		Assert.True(ready, "The app did not connect and load the tree. Log:\n" + _app.ReadLog());
		Assert.True(await _rows.WaitForRowAsync(process, "Anchor", shouldExist: true, StepTimeout), "The group's rows are not shown. Log:\n" + _app.ReadLog());
		return process;
	}

	/// <summary>The registry call of <c>create_session</c> (FR.15.15): right after the anchor, Opus, Max, the Default profile, streaming and sound on.</summary>
	private SessionNodeModel CreateAfterAnchor()
	{
		var placed = _env.Registry.CreateTopLevelSession(
			new NewSessionPlacementModel(_anchor.NodeId, null, null),
			NewName,
			new NewSessionSettingsModel(Opus, "max", "default", null, IsStreamingMode: true, PlayCompletionSound: true));
		Assert.NotNull(placed);
		return placed.Session;
	}

	/// <summary>The selected rows that are sessions of this test (a closed selector can expose its selected option as a list item too).</summary>
	private List<string> SelectedSessions(Process process) => _bar.GetSelectedRowNames(process).Where(SessionRows.Contains).ToList();

	private async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !condition())
			await Task.Delay(200);

		Assert.True(condition(), $"{what}. Log:\n" + _app.ReadLog());
	}

	[Fact]
	public async Task A_created_session_appears_right_under_its_anchor_and_the_selection_stays()
	{
		using var process = await StartAsync();
		try
		{
			Assert.Equal(["Main session"], SelectedSessions(process));

			CreateAfterAnchor();
			Assert.True(await _rows.WaitForRowAsync(process, NewName, shouldExist: true, StepTimeout), "The new row never appeared. Log:\n" + _app.ReadLog());

			var order = _bar.GetRowNames(process).Where(n => n is "First" or "Anchor" or NewName or "Last").ToList();
			Assert.Equal(["First", "Anchor", NewName, "Last"], order);
			Assert.Equal(["Main session"], SelectedSessions(process));

			var log = _app.ReadLog();
			Assert.DoesNotContain("[ERR]", log);
			Assert.DoesNotContain("[FTL]", log);
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Opening_the_created_session_shows_its_model_effort_and_streaming()
	{
		using var process = await StartAsync();
		try
		{
			var created = CreateAfterAnchor();
			Assert.True(await _rows.SelectAsync(process, NewName, StepTimeout), "The new row could not be selected. Log:\n" + _app.ReadLog());
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, $"Session {created.NodeId} opened"), "The new session did not open. Log:\n" + _app.ReadLog());

			await WaitForAsync(() => _bar.GetSelectedItem(process, "Model") == "Opus 5.5", $"Model shows '{_bar.GetSelectedItem(process, "Model")}', not its own model");
			await WaitForAsync(() => _bar.GetSelectedItem(process, "Effort") == "Max", $"Effort shows '{_bar.GetSelectedItem(process, "Effort")}', not its own effort");
			await WaitForAsync(() => _bar.GetToggleState(process, "Streaming Session Mode") == ToggleState.On, "Streaming Session Mode is not on");

			var log = _app.ReadLog();
			Assert.DoesNotContain("[ERR]", log);
			Assert.DoesNotContain("[FTL]", log);
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
