using System.Diagnostics;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the Session/Focus switch and the collapsible input area (FR.12.29, FR.23.12) and for the Focus reply drafts (FR.23.19): the BUILT WPF app against a real
/// loopback daemon with the real attention and draft modules. The switch and the buttons are used through UI Automation; the draft files are read from the daemon's draft store.
/// The double click on the splitter has no UI Automation pattern, so the collapse is observed through what the switch changes: the buttons of the side column.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfInputAreaE2ETests : IDisposable
{
	private const string ReplyBoxName = "Your answer";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-inputarea-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly DraftService _drafts;
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly string _main;

	public WpfInputAreaE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_main = _env.Session.NodeId;
		_drafts = new DraftService(Path.Combine(_root, "drafts"));

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher(
			[new ClientRpcModule(runInfo: run, eventLog: log, drafts: _drafts), .. _env.Modules, new DraftRpcModule(_env.Base.Registry, _drafts, _env.Attention), tree, new CatalogTestModule([])]));

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

	private async Task<Process> StartAsync(int openItems = 0)
	{
		var process = _app.Start();
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", $"Focus: {openItems} open item(s) for {_main}");
		Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task PostAsync(string title) =>
		Assert.DoesNotContain("error", (await _env.PostAsync(new JsonObject { ["kind"] = "ask", ["title"] = title })).ToJsonString(), StringComparison.OrdinalIgnoreCase);

	private async Task<bool> WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
				return true;

			await Task.Delay(100);
		}

		return condition();
	}

	[Fact]
	public async Task A_session_opens_on_the_Session_view_with_the_input_area_expanded()
	{
		using var process = await StartAsync();
		try
		{
			Assert.True(await _clicker.WaitForButtonAsync(process, "Settings", StepTimeout), "The expanded input area shows the settings toggle. Log:\n" + _app.ReadLog());
			Assert.True(_clicker.Exists(process, "Send"));
			Assert.True(_clicker.Exists(process, "File mode"));
			Assert.False(_clicker.Exists(process, "Stop"));
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Bringing_the_Focus_panel_forward_leaves_the_input_area_of_the_Session_view_expanded()
	{
		// The docked shell (FR.6.17): the composer belongs to the Session view and no longer collapses when Focus is shown.
		using var process = await StartAsync();
		try
		{
			Assert.True(await _clicker.ClickAsync(process, "Focus", StepTimeout));
			await Task.Delay(500);
			Assert.True(await _clicker.WaitForButtonAsync(process, "Settings", StepTimeout), "The settings toggle of the expanded input area is gone. Log: " + _app.ReadLog());
			Assert.True(_clicker.Exists(process, "File mode"));
			Assert.True(_clicker.Exists(process, "Send"));
			Assert.False(_clicker.Exists(process, "Stop"));
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task The_settings_toggle_hides_and_shows_the_command_bar_of_the_expanded_area()
	{
		using var process = await StartAsync();
		try
		{
			Assert.True(await _clicker.WaitForButtonAsync(process, "Settings", StepTimeout));
			await Task.Delay(500);
			Assert.True(await _clicker.ToggleAsync(process, "Settings", StepTimeout), "The settings toggle was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ToggleAsync(process, "Settings", StepTimeout));
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_reply_typed_in_the_Focus_view_is_there_after_the_app_is_closed_and_started_again()
	{
		await PostAsync("Proceed with the migration?");
		using (var first = await StartAsync(openItems: 1))
		{
			try
			{
				Assert.True(await _clicker.ClickAsync(first, "Focus", StepTimeout));
				Assert.True(await _clicker.SetTextAsync(first, ReplyBoxName, "Yes, but only on staging", StepTimeout), "The reply box was not found. Log:\n" + _app.ReadLog());
			}
			finally
			{
				_app.Stop(first);   // closing flushes the draft (FR.23.19)
			}
		}

		Assert.True(await WaitForAsync(() => _drafts.ListItemDraftIds(_env.Session.FileName).Count == 1), "The reply draft did not reach the daemon's draft store. Log:\n" + _app.ReadLog());

		using var second = await StartAsync(openItems: 1);
		try
		{
			Assert.True(await _clicker.ClickAsync(second, "Focus", StepTimeout));
			Assert.True(await WaitForAsync(() => _clicker.GetText(second, ReplyBoxName) == "Yes, but only on staging"), "The reply text was not restored. Log:\n" + _app.ReadLog());
		}
		finally
		{
			_app.Stop(second);
		}
	}

	[Fact]
	public async Task A_sent_reply_leaves_no_draft_behind()
	{
		await PostAsync("Proceed with the migration?");
		using var process = await StartAsync(openItems: 1);
		try
		{
			Assert.True(await _clicker.ClickAsync(process, "Focus", StepTimeout));
			Assert.True(await _clicker.SetTextAsync(process, ReplyBoxName, "Go", StepTimeout));
			Assert.True(await _clicker.ClickAsync(process, "Send answers", StepTimeout), "Send answers was not available. Log:\n" + _app.ReadLog());

			Assert.True(await WaitForAsync(() => _env.Attention.GetItems(_main).Single().Status == AttentionStatus.Answered));
			Assert.True(await WaitForAsync(() => _drafts.ListItemDraftIds(_env.Session.FileName).Count == 0), "A draft was left behind. Log:\n" + _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
