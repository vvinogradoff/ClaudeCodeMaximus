using System.Diagnostics;
using System.Windows.Automation;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for FR.9.6 / FR.1.15 (ISSUE.495): picking a session in the Recent dropdown selects it in the session tree, expands its collapsed ancestors and
/// scrolls the tree so its row is visible, as a click on the tree row would. The BUILT WPF app runs against a real loopback daemon; the tree rows are read
/// through UI Automation (selected state and position against the list that holds them).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfRecentTreeSyncE2ETests : IDisposable
{
	private const int Sessions = 40;
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-recenttree-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly SessionNodeModel _last;
	private readonly SessionNodeModel _deep;
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();

	public WpfRecentTreeSyncE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies("question 0", "answer 1");
		var all = new List<SessionNodeModel> { _env.Session };
		for (var i = 1; i < Sessions; i++)
			all.Add(_env.AddSession($"Filler session {i:00}", "question", "answer"));

		_last = all[^1];
		var group = _env.Base.Registry.AddGroup(_env.Base.Directory, "Archive");   // collapsed: nothing lists it as expanded
		_deep = _env.Base.Registry.AddSessionToGroup(group, "Deep session");
		_env.WriteBodies(_deep, "question", "answer");
		all.Add(_deep);
		var recent = new RecentTestModule([_deep, _last, .. all.Where(n => n != _deep && n != _last)]);   // the two targets head the dropdown (it lists in the given order), far down the tree

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, TestEngines.Claude(new StubStreamingManager()), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree, recent, new CatalogTestModule([])]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_env.Session.NodeId}\",\"expandedNodeIds\":[\"{_env.Base.Directory.NodeId}\"]}}");
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
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_env.Session.NodeId} opened");
		Assert.True(ready, "The app did not open the session. Log:" + Environment.NewLine + _app.ReadLog());
		return process;
	}

	/// <summary>What the tree shows for the row of <paramref name="name"/>: whether the row exists (is realised), is selected, and lies inside the visible part of its list.</summary>
	private (bool Exists, bool Selected, bool Visible) RowState(Process process, string name)
	{
		var hwnd = new AppWindowFinder().Find(process.Id);
		if (hwnd == IntPtr.Zero)
			return (false, false, false);

		try
		{
			var row = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
				new PropertyCondition(AutomationElement.NameProperty, name)));
			if (row == null)
				return (false, false, false);

			var selected = row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern) && ((SelectionItemPattern)pattern).Current.IsSelected;
			var list = TreeWalker.ControlViewWalker.GetParent(row);
			var inside = list != null && !row.Current.IsOffscreen && !row.Current.BoundingRectangle.IsEmpty
				&& list.Current.BoundingRectangle.Contains(row.Current.BoundingRectangle);
			return (true, selected, inside);
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return (false, false, false);
		}
	}

	private async Task PickFromDropdownAsync(Process process, SessionNodeModel session)
	{
		Assert.True(await _clicker.ToggleAsync(process, "Recent dropdown", StepTimeout), "The dropdown button was not found. Log:" + Environment.NewLine + _app.ReadLog());
		Assert.True(await _clicker.ClickAsync(process, "Open " + session.Name, StepTimeout), $"The row of {session.Name} was not found in the dropdown. Log:" + Environment.NewLine + _app.ReadLog());
		Assert.True(await _app.WaitForLogAsync(process, StepTimeout, $"Session {session.NodeId} opened"), "The session did not open. Log:" + Environment.NewLine + _app.ReadLog());
	}

	private async Task ExpectSelectedAndVisibleAsync(Process process, string name)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		var state = RowState(process, name);
		while (DateTime.UtcNow < deadline && !(state.Exists && state.Selected && state.Visible))
		{
			await Task.Delay(250);
			state = RowState(process, name);
		}

		Assert.True(state.Exists, $"The tree has no row for {name} (an ancestor was not expanded, or the row was not scrolled to). Log:" + Environment.NewLine + _app.ReadLog());
		Assert.True(state.Selected, $"The row of {name} is not selected in the tree.");
		Assert.True(state.Visible, $"The row of {name} is selected but outside the visible part of the tree (the tree was not scrolled).");
	}

	[Fact]
	public async Task Picking_a_session_far_down_the_tree_from_the_dropdown_selects_it_and_scrolls_the_tree_to_it()
	{
		using var process = await StartAsync();
		try
		{
			Assert.False(RowState(process, _last.Name).Visible, "Precondition: the last session starts below the visible part of the tree.");

			await PickFromDropdownAsync(process, _last);

			await ExpectSelectedAndVisibleAsync(process, _last.Name);
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Picking_a_session_inside_a_collapsed_group_from_the_dropdown_expands_the_group_selects_the_row_and_shows_it()
	{
		using var process = await StartAsync();
		try
		{
			Assert.False(RowState(process, _deep.Name).Exists, "Precondition: the group is collapsed, so its session has no row.");

			await PickFromDropdownAsync(process, _deep);

			await ExpectSelectedAndVisibleAsync(process, _deep.Name);
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
