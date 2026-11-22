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
/// End to end for the Focus panel's header (TC.DS.27, 30, 31, 39; FR.6.23, UC.DS.13): the BUILT WPF app against a real loopback daemon (real tree, session and tree mutation modules). The
/// layout is driven through the docking test hook (Focus tabbed over the session area, floating), the buttons through UI Automation. While Focus is tabbed over the Session view only the Focus
/// header's controls are visible, so a control found by name is the Focus header's. The double-click rename of the name cannot be driven from the never-activated test window (posted mouse
/// messages are not delivered to it); it is covered by the view tests (FocusHeaderViewTests) and stays MANUAL here (TC.DS.28). A floating Focus panel's frame exposes no content to UI Automation in the test window (the frame shows only its title and empty panes), so the header in a floating or slid-out Focus panel stays MANUAL (TC.DS.37).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfFocusHeaderE2ETests : IDisposable
{
	private const string Session = "session";
	private const string Focus = "focus";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-focus-header-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly RemoteSessionEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();

	public WpfFocusHeaderE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.Files.Set(_env.Session.FileName, [User("hello", 0), Assistant("hi", 1, "claude-opus-5-5", "high")]);

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Registry, TestEngines.Claude(_env.Streaming), _env.Attention, activity);
		var versions = new TreeVersionTracker();
		_env.Registry.TreeChanged += (_, e) =>
		{
			if (e.Kind != TreeChangeKind.DaemonMutation && e.Kind != TreeChangeKind.SessionIsRunningChanged)
				versions.Increment();
			else if (e.Kind == TreeChangeKind.DaemonMutation)
				_host!.Bus.Publish(RemoteProtocol.Events.TreeChanged, new TreeChangedPayload(versions.Current));
		};
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Settings), _env.Registry, states, activity, versions,
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		var mutation = new TreeMutationService(_env.Registry, _env.Settings, new FakeDirectoryLabelService(), new FakeDirectoryGitOrigins());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([
			new ClientRpcModule(runInfo: run, eventLog: log, treeMutation: mutation), _env.Module, tree, new TreeMutationRpcModule(mutation)]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_env.Session.NodeId}\",\"expandedNodeIds\":[\"{_env.Directory.NodeId}\"]}}");
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

	private static string[] Dialogs(params string[] answers) => answers.SelectMany(a => new[] { "--test-dialog", a }).ToArray();

	private async Task<Process> StartAsync(params string[] arguments)
	{
		var process = _app.Start(new Dictionary<string, string>(), ["--test-monitors", "1", .. arguments]);
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", "Tree loaded:", $"Session {_env.Session.NodeId} opened");
		Assert.True(ready, "The app did not connect and open the session. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task PressAsync(Process process, string button) =>
		Assert.True(await _clicker.ClickAsync(process, button, StepTimeout), $"The '{button}' button was not found. Log:\n" + _app.ReadLog());

	private static async Task<bool> WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
				return true;

			await Task.Delay(200);
		}

		return false;
	}

	private bool HasSession()
	{
		lock (_env.Settings.SyncRoot)
			return _env.Directory.Sessions.Any(s => s.NodeId == _env.Session.NodeId);
	}

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	/// <summary>True when a window of the app (the main one or a floating panel's) holds an element with this name and control type.</summary>
	private static bool ExistsInAnyWindow(Process process, string name, ControlType type) =>
		new AppWindowFinder().FindAll(process.Id).Any(hwnd => AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
			new PropertyCondition(AutomationElement.ControlTypeProperty, type),
			new PropertyCondition(AutomationElement.NameProperty, name))) != null);

	private static async Task<WpfDockingHookClient> TabFocusOverSessionAsync(Process process)
	{
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		Assert.True((await hook.TabAsync(Focus, Session)).Ok);
		await hook.BringForwardAsync(Focus);
		await hook.WaitStateAsync(r => r.Panels.Single(p => p.Id == Focus).Selected && !r.Panels.Single(p => p.Id == Session).Selected, "Focus tabbed over the Session view");
		return hook;
	}

	[Fact]
	public async Task TC_DS_39_the_Session_button_brings_the_Session_view_forward_and_the_Focus_button_brings_Focus_back()
	{
		using var process = await StartAsync();
		try
		{
			using var hook = await TabFocusOverSessionAsync(process);
			Assert.True(_clicker.Exists(process, "Session"), "The Session button of the Focus header was not found");
			Assert.False(_clicker.Exists(process, "Focus"), "The Session view's Focus button must be hidden while Focus covers the Session view");

			await PressAsync(process, "Session");
			await hook.WaitStateAsync(r => r.Panels.Single(p => p.Id == Session).Selected && !r.Panels.Single(p => p.Id == Focus).Selected, "the Session view selected after the Session button");

			await PressAsync(process, "Focus");
			await hook.WaitStateAsync(r => r.Panels.Single(p => p.Id == Focus).Selected && !r.Panels.Single(p => p.Id == Session).Selected, "Focus selected again after the Focus button");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task TC_DS_27_the_Focus_header_shows_the_session_name_Delete_the_star_and_Send_answers_with_Session_at_its_end()
	{
		using var process = await StartAsync();
		try
		{
			using var hook = await TabFocusOverSessionAsync(process);
			foreach (var name in new[] { "Delete session", "Favourite", "Send answers", "Session" })
				Assert.True(await _clicker.WaitForButtonAsync(process, name, StepTimeout), $"The '{name}' button of the Focus header was not found. Log:\n" + _app.ReadLog());

			Assert.True(ExistsInAnyWindow(process, _env.Session.Name, ControlType.Text), "The session's name is not shown in the Focus header");
			Assert.True(ExistsInAnyWindow(process, "Session state", ControlType.Text), "The Running / Idle text is not in the Focus header");
			foreach (var absent in new[] { "Back", "Send later" })
				Assert.False(_clicker.Exists(process, absent), $"'{absent}' is not part of the Focus header");

			var send = _clicker.GetBounds(process, "Send answers");
			var session = _clicker.GetBounds(process, "Session");
			var star = _clicker.GetBounds(process, "Favourite");
			Assert.NotNull(send);
			Assert.NotNull(session);
			Assert.NotNull(star);
			Assert.True(star.Value.Right <= send.Value.Left + 1 && send.Value.Right <= session.Value.Left + 1, "Order: star, Send answers (n), Session at the right end");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task TC_DS_31_the_star_in_Focus_sets_the_flag_the_Session_views_star_clears()
	{
		using var process = await StartAsync();
		try
		{
			using var hook = await TabFocusOverSessionAsync(process);
			Assert.False(_env.Session.IsFavorite);

			await PressAsync(process, "Favourite");   // the Focus header's: it covers the Session view
			Assert.True(await WaitForAsync(() => _env.Session.IsFavorite), "The daemon did not get the favourite flag from the Focus star. Log:\n" + _app.ReadLog());

			await PressAsync(process, "Session");
			await hook.WaitStateAsync(r => r.Panels.Single(p => p.Id == Session).Selected, "the Session view forward");
			await PressAsync(process, "Favourite");   // now the Session view's star
			Assert.True(await WaitForAsync(() => !_env.Session.IsFavorite), "The Session view's star did not clear the flag the Focus star set. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task TC_DS_30_Delete_in_Focus_asks_first_Cancel_keeps_the_session_and_confirming_removes_it()
	{
		using var process = await StartAsync(Dialogs("cancel", "accept"));
		try
		{
			using var hook = await TabFocusOverSessionAsync(process);

			await PressAsync(process, "Delete session");
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "dialog 'Delete' (\"Confirm\") answered with cancel"), "The delete was not asked about. Log:\n" + _app.ReadLog());
			Assert.True(HasSession(), "Cancel must keep the session");

			await PressAsync(process, "Delete session");
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "dialog 'Delete' (\"Confirm\") answered with accept"), "The second delete was not asked about. Log:\n" + _app.ReadLog());
			Assert.True(await WaitForAsync(() => !HasSession()), "The daemon still has the session after the confirmed delete. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
