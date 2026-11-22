using System.Diagnostics;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the tree operations (W2): the BUILT WPF app against a real loopback daemon (the real <see cref="TdcpServer"/> with the real tree, session and J1 tree
/// mutation modules over a seeded temp registry). The toolbar buttons and the rows are driven through UI Automation (the test window is off-screen and never activated);
/// the questions the operations ask are answered from the <c>--test-dialog</c> script and the folder dialog from <c>--test-folder</c>. Every outcome is checked in the
/// daemon's registry, not only on screen.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfTreeOperationsE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-tree-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly RemoteSessionEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly UiAutomationTree _rows = new();
	private readonly SessionNodeModel _withFile;
	private readonly SessionNodeModel _noFile;
	private readonly GroupNodeModel _archive;
	private readonly GroupNodeModel _beta;
	private readonly GroupNodeModel _gamma;
	private readonly TreeMutationService _mutation;

	public WpfTreeOperationsE2ETests()
	{
		Directory.CreateDirectory(_root);

		// proj: "Main session" and "With transcript" have a transcript file; "No transcript" has none; the group "Archive" is empty.
		_env.Files.Set(_env.Session.FileName, [User("hello", 0), Assistant("hi", 1, "claude-opus-5-5", "high")]);
		_withFile = _env.Registry.AddSession(_env.Directory, "With transcript");
		_env.Files.Set(_withFile.FileName, [User("a question", 0), Assistant("an answer", 1, "claude-opus-5-5", "high")]);
		_noFile = _env.Registry.AddSession(_env.Directory, "No transcript");
		_archive = _env.Registry.AddGroup(_env.Directory, "Archive");
		_beta = _env.Registry.AddGroup(_env.Directory, "Beta group");
		_gamma = _env.Registry.AddGroup(_env.Directory, "Gamma group");

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Registry, TestEngines.Claude(_env.Streaming), _env.Attention, activity);
		var versions = new TreeVersionTracker();
		// What the daemon's notification publisher does: a structural change of the tree is a new tree version.
		_env.Registry.TreeChanged += (_, e) =>
		{
			if (e.Kind != TreeChangeKind.DaemonMutation && e.Kind != TreeChangeKind.SessionIsRunningChanged)
				versions.Increment();
			else if (e.Kind == TreeChangeKind.DaemonMutation)
				_host!.Bus.Publish(RemoteProtocol.Events.TreeChanged, new TreeChangedPayload(versions.Current));   // the publisher's tree.changed after a mutation
		};
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Settings), _env.Registry, states, activity, versions,
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_mutation = new TreeMutationService(_env.Registry, _env.Settings, new FakeDirectoryLabelService(), new FakeDirectoryGitOrigins());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([
			new ClientRpcModule(runInfo: run, eventLog: log, treeMutation: _mutation), _env.Module, tree, new TreeMutationRpcModule(_mutation)]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		// Client state as a previous run left it: the directory expanded, the main session selected.
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

	private async Task<Process> StartAsync(params string[] arguments)
	{
		var process = _app.Start(arguments);
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", "Tree loaded:", $"Session {_env.Session.NodeId} opened");
		Assert.True(ready, "The app did not connect and load the tree. Log:\n" + _app.ReadLog());
		return process;
	}

	private static string[] Dialogs(params string[] answers) => answers.SelectMany(a => new[] { "--test-dialog", a }).ToArray();

	private async Task PressAsync(Process process, string button)
	{
		Assert.True(await _clicker.ClickAsync(process, button, StepTimeout), $"The '{button}' button was not found. Log:\n" + _app.ReadLog());
	}

	private async Task SelectAsync(Process process, string row)
	{
		Assert.True(await _rows.SelectAsync(process, row, StepTimeout), $"The row '{row}' was not found. Log:\n" + _app.ReadLog());
	}

	private async Task AssertRowAsync(Process process, string row, bool shouldExist)
	{
		Assert.True(await _rows.WaitForRowAsync(process, row, shouldExist, StepTimeout), $"The row '{row}' {(shouldExist ? "never appeared" : "never went away")}. Log:\n" + _app.ReadLog());
	}

	private async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !condition())
			await Task.Delay(100);

		Assert.True(condition(), $"{what}. Log:\n" + _app.ReadLog());
	}

	private IEnumerable<SessionNodeModel> AllSessions()
	{
		lock (_env.Settings.SyncRoot)
		{
			var found = new List<SessionNodeModel>(_env.Directory.Sessions);
			void Walk(GroupNodeModel group)
			{
				found.AddRange(group.Sessions);
				foreach (var child in group.Groups)
					Walk(child);
			}

			foreach (var group in _env.Directory.Groups)
				Walk(group);

			return found;
		}
	}

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	[Fact]
	public async Task The_session_header_has_the_Delete_icon_and_the_toolbar_no_longer_has_Rename_Delete_or_Move_to()
	{
		using var process = await StartAsync();
		try
		{
			await SelectAsync(process, "No transcript");
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, $"Session {_noFile.NodeId} opened"), "The session was not opened. Log:\n" + _app.ReadLog());

			Assert.True(_clicker.Exists(process, "Delete session"), "The delete icon of the header was not found");
			foreach (var gone in new[] { "Rename", "Delete", "Move to" })
				Assert.False(_clicker.Exists(process, gone), $"The '{gone}' toolbar button is still there");
			foreach (var kept in new[] { "Add directory", "New session", "New group", "Refresh git origins" })
				Assert.True(_clicker.Exists(process, kept), $"The '{kept}' toolbar button is missing");

			// The double click on the title and the context menu cannot be driven from the never-activated test window (posted mouse messages are not delivered to it); both are
			// covered by the view and view model tests (TreeFeedbackTests).
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Delete_asks_why_it_is_blocked_keeps_the_session_on_No_and_removes_it_on_Yes()
	{
		using var process = await StartAsync(Dialogs("cancel", "accept"));
		try
		{
			await SelectAsync(process, "With transcript");

			// First delete: the session has a transcript file, so the daemon refuses and the user is asked. The scripted answer is "cancel".
			await PressAsync(process, "Delete session");
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "dialog 'Delete' (\"Confirm\") answered with cancel"), "The delete was not asked about. Log:\n" + _app.ReadLog());
			Assert.Contains(_withFile.NodeId, AllSessions().Select(s => s.NodeId));
			await AssertRowAsync(process, "With transcript", shouldExist: true);

			// Second delete: the scripted answer is "accept", so the delete is repeated with force.
			await PressAsync(process, "Delete session");
			await AssertRowAsync(process, "With transcript", shouldExist: false);
			await WaitForAsync(() => !AllSessions().Any(s => s.NodeId == _withFile.NodeId), "The daemon still has the session");
			Assert.Contains("dialog 'Delete' (\"Confirm\") answered with accept", _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Delete_of_a_session_without_a_transcript_needs_no_question()
	{
		using var process = await StartAsync();
		try
		{
			await SelectAsync(process, "No transcript");
			await PressAsync(process, "Delete session");

			await AssertRowAsync(process, "No transcript", shouldExist: false);
			await WaitForAsync(() => !AllSessions().Any(s => s.NodeId == _noFile.NodeId), "The daemon still has the session");
			Assert.DoesNotContain("Test window: dialog", _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	// The Ctrl+Up / Ctrl+Down half of TC.TO.17 cannot be sent to the never-activated test window (WS_EX_NOACTIVATE, no keyboard focus): it is covered by
	// TreeOrderTests (view model and STA key event, TessynDesktop.WPF.Tests) and TreeMutationServiceTests (the real daemon's index mapping).
	public async Task NewSession_creates_a_session_in_the_daemon_selects_it_and_opens_it_in_the_pane()
	{
		using var process = await StartAsync(Dialogs("text:Brand new session"));
		try
		{
			await SelectAsync(process, "No transcript");
			await PressAsync(process, "New session");

			await AssertRowAsync(process, "Brand new session", shouldExist: true);
			await WaitForAsync(() => AllSessions().Any(s => s.Name == "Brand new session"), "The daemon has no new session");
			var created = AllSessions().Single(s => s.Name == "Brand new session");

			// TC.TO.17 / FR.1.30: with a session selected the new one is the LAST session of its directory in the daemon (not first, not beside the selection).
			lock (_env.Settings.SyncRoot)
				Assert.Same(created, _env.Directory.Sessions.Last());
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, $"Session {created.NodeId} opened"), "The new session was not opened in the pane. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	/// <summary>TC.TO.24 (FR.1.32): a group moved through <c>tree.moveNode</c> shows in its new place in the app after <c>tree.changed</c>. The key press itself cannot be sent to the never-activated test window; it is covered by GroupOrderTests.</summary>
	[Fact]
	public async Task A_group_moved_through_the_daemon_shows_in_its_new_place_in_the_app()
	{
		using var process = await StartAsync();
		try
		{
			string[] groups = ["Archive", "Beta group", "Gamma group"];
			Assert.True(await _rows.WaitForOrderAsync(process, groups, StepTimeout), "The groups are not in their first order. Log:\n" + _app.ReadLog());

			var result = _mutation.MoveNode(_gamma.NodeId, _env.Directory.NodeId, 0);
			Assert.True(result.Moved);
			Assert.True(await _rows.WaitForOrderAsync(process, ["Gamma group", "Archive", "Beta group"], StepTimeout), "The app does not show the group in its new place. Log:\n" + _app.ReadLog());
			lock (_env.Settings.SyncRoot)
				Assert.Equal(["Gamma group", "Archive", "Beta group"], _env.Directory.Groups.Select(g => g.Name).ToArray());

			// Another parent is refused, and nothing moves.
			Assert.Throws<TdcpException>(() => _mutation.MoveNode(_beta.NodeId, _archive.NodeId, 0));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task NewGroup_creates_a_group_in_the_daemon_and_shows_it()
	{
		using var process = await StartAsync(Dialogs("text:Fresh group"));
		try
		{
			await SelectAsync(process, "No transcript");
			await PressAsync(process, "New group");
			await AssertRowAsync(process, "Fresh group", shouldExist: true);
			await WaitForAsync(() => { lock (_env.Settings.SyncRoot) return _env.Directory.Groups.Any(g => g.Name == "Fresh group"); }, "The daemon has no new group");

			// Move to is an item of the context menu of a session now; the menu cannot be opened from the never-activated test window, its command is covered by the
			// tree operation tests.
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task AddDirectory_uses_the_clients_folder_dialog_and_shows_the_daemons_refusal_of_a_broad_folder()
	{
		var folder = Path.Combine(_root, "added-folder");
		Directory.CreateDirectory(folder);
		using var process = await StartAsync(["--test-folder", folder, "--test-folder", @"C:\", .. Dialogs("accept")]);
		try
		{
			await PressAsync(process, "Add directory");
			await AssertRowAsync(process, folder, shouldExist: true);
			await WaitForAsync(() => { lock (_env.Settings.SyncRoot) return _env.Registry.Directories.Any(d => string.Equals(d.Path, folder, StringComparison.OrdinalIgnoreCase)); }, "The daemon has no new directory");

			// A drive root is refused by the daemon; its message is shown in a dialog (answered "accept" by the script).
			await PressAsync(process, "Add directory");
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "dialog 'Cannot add this directory' (\"Confirm\") answered with accept"), "The refusal was not shown. Log:\n" + _app.ReadLog());
			Assert.DoesNotContain(_env.Registry.Directories, d => d.Path == @"C:\" || d.Path == "C:");
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
