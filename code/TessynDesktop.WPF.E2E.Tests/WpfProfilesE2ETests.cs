using System.Diagnostics;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the profile switch (W5, ISSUE.013 R1): the BUILT WPF app against a real loopback daemon (real tree and session modules over a seeded registry, plus a
/// test <c>profile.*</c> module that records what the client sends). The "Switch profile" button is pressed through UI Automation; the pick, the cascade question and the
/// report are answered from the <c>--test-dialog</c> script. The daemon's switching itself is covered by its own tests.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfProfilesE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-profiles-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly RemoteSessionEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly UiAutomationTree _rows = new();
	private readonly ProfileTestModule _profiles = new();
	private readonly SessionNodeModel _worker;

	public WpfProfilesE2ETests()
	{
		Directory.CreateDirectory(_root);

		_env.Files.Set(_env.Session.FileName, [User("hello", 0), Assistant("hi", 1, "claude-opus-5-5", "high")]);
		_worker = _env.Registry.AddSession(_env.Directory, "Worker one");
		lock (_env.Settings.SyncRoot)
			_worker.SupervisorNodeId = _env.Session.NodeId;
		_env.Files.Set(_worker.FileName, [User("work", 0), Assistant("done", 1, "claude-opus-5-5", "high")]);

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Registry, _env.Streaming, _env.Attention, activity);
		var versions = new TreeVersionTracker();
		_env.Registry.TreeChanged += (_, e) =>
		{
			if (e.Kind != TreeChangeKind.DaemonMutation && e.Kind != TreeChangeKind.SessionIsRunningChanged)
				versions.Increment();
		};
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Settings), _env.Registry, states, activity, versions,
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		var mutation = new TreeMutationService(_env.Registry, _env.Settings, new FakeDirectoryLabelService(), new FakeDirectoryGitOrigins());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([
			new ClientRpcModule(runInfo: run, eventLog: log, treeMutation: mutation, profileAdmin: new AnnouncingProfileAdmin()), _env.Module, tree, new TreeMutationRpcModule(mutation), _profiles]));

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

	private async Task<Process> StartAsync(params string[] arguments)
	{
		var process = _app.Start(arguments);
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", "Tree loaded:", $"Session {_env.Session.NodeId} opened");
		Assert.True(ready, "The app did not connect and load the tree. Log:\n" + _app.ReadLog());
		return process;
	}

	private static string[] Dialogs(params string[] answers) => answers.SelectMany(a => new[] { "--test-dialog", a }).ToArray();

	private async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !condition())
			await Task.Delay(100);

		Assert.True(condition(), $"{what}. Log:\n" + _app.ReadLog());
	}

	[Fact]
	public async Task SwitchingASessionWithWorkers_AsksTheCascade_SendsIncludeDescendants_AndReportsTheQueuedWorker()
	{
		_profiles.SwitchAnswer = (node, profile, include) => new ProfileSwitchResultDto(
			profile, "Work", NodeMoved: true, Moved: [], Queued: [new ProfileSwitchedSessionDto(_worker.NodeId, "Worker one", "default", "[Sys] test@example.com")], OldConfigPaths: [], Failed: []);
		using var process = await StartAsync(Dialogs("choice:profile_1", "choice:all", "accept"));
		try
		{
			Assert.True(await _rows.SelectAsync(process, _env.Session.Name, StepTimeout), "The session row was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Switch profile", StepTimeout), "The Switch profile button was not found. Log:\n" + _app.ReadLog());

			await WaitForAsync(() => _profiles.Switches.Count == 1, "The daemon got no profile.switch");
			Assert.Equal((_env.Session.NodeId, "profile_1", true), _profiles.Switches[0]);
			var log = _app.ReadLog();
			Assert.Contains("dialog 'Switch profile' (\"Choice\") answered with choice:profile_1", log);
			Assert.Contains("dialog 'Switch profile' (\"Choice\") answered with choice:all", log);
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "dialog 'Profile switch' (\"Confirm\") answered with accept"), "The report of the queued worker was not shown. Log:\n" + _app.ReadLog());
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task SwitchingOnlyThisSession_SendsNoDescendants()
	{
		using var process = await StartAsync(Dialogs("choice:profile_2", "choice:only"));
		try
		{
			Assert.True(await _rows.SelectAsync(process, _env.Session.Name, StepTimeout), "The session row was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Switch profile", StepTimeout), "The Switch profile button was not found. Log:\n" + _app.ReadLog());

			await WaitForAsync(() => _profiles.Switches.Count == 1, "The daemon got no profile.switch");
			Assert.Equal((_env.Session.NodeId, "profile_2", false), _profiles.Switches[0]);
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task CancellingTheCascade_SwitchesNothing()
	{
		using var process = await StartAsync(Dialogs("choice:profile_1", "cancel"));
		try
		{
			Assert.True(await _rows.SelectAsync(process, _env.Session.Name, StepTimeout), "The session row was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Switch profile", StepTimeout), "The Switch profile button was not found. Log:\n" + _app.ReadLog());

			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "answered with cancel"), "The cascade was not asked. Log:\n" + _app.ReadLog());
			Assert.Empty(_profiles.Switches);
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
