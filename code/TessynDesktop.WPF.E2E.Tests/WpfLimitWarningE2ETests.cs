using System.Diagnostics;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the limits-error warning (FR.1.29, UC.LE.01, TC.LE.18): the BUILT WPF app against a real loopback daemon with the real tree module, the real registry, the real
/// <see cref="SessionStateBuilder"/> and the real <see cref="SessionAuthErrorTracker"/>. A limits error recorded for a worker and for a top-level session is announced as the
/// <c>session.changed</c> the daemon's publisher sends; the yellow warning appears on the row and goes away when the error is cleared by a later successful turn.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfLimitWarningE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-workers-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly RemoteSessionEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly SessionAuthErrorTracker _tracker = new();
	private readonly SessionNodeModel _worker;

	public WpfLimitWarningE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.Files.Set(_env.Session.FileName, [User("hello", 0), Assistant("hi", 1, "claude-opus-5-5", "high")]);
		_worker = _env.Registry.AddSession(_env.Directory, "Worker one");
		_worker.SupervisorNodeId = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Registry, TestEngines.Claude(_env.Streaming), _env.Attention, activity, _tracker);
		var versions = new TreeVersionTracker();
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Settings), _env.Registry, states, activity, versions,
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), _env.Module, tree]));

		// What the daemon's publisher does when the limits error of a session changes: session.changed with the fresh state.
		_tracker.Changed += nodeId =>
		{
			var node = _env.Registry.FindModelByNodeId(nodeId);
			if (node != null)
				_host.Bus.Publish(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(states.Build(node)));
		};

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_env.Session.NodeId}\",\"expandedNodeIds\":[\"{_env.Directory.NodeId}\",\"{_env.Session.NodeId}\"]}}");
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
		return process;
	}

	private async Task WaitForWarningAsync(Process process, bool shouldExist)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && _clicker.TextExists(process, "Limits error") != shouldExist)
			await Task.Delay(200);

		Assert.True(_clicker.TextExists(process, "Limits error") == shouldExist, $"The warning {(shouldExist ? "never appeared" : "never went away")}. Log:\n" + _app.ReadLog());
	}

	[Fact]
	public async Task The_warning_appears_on_a_session_and_on_a_worker_and_goes_away_when_cleared()
	{
		using var process = await StartAsync();
		try
		{
			Assert.False(_clicker.TextExists(process, "Limits error"), "No limits error: no warning");

			_tracker.SetLimitError(_worker.NodeId, "You've hit your session limit · resets 5:20pm (Asia/Makassar)");
			await WaitForWarningAsync(process, shouldExist: true);
			_tracker.SetLimitError(_worker.NodeId, null);   // a later turn succeeded
			await WaitForWarningAsync(process, shouldExist: false);

			_tracker.SetLimitError(_env.Session.NodeId, "You've hit your weekly limit · resets Nov 11, 1pm (Asia/Makassar)");
			await WaitForWarningAsync(process, shouldExist: true);
			_tracker.SetLimitError(_env.Session.NodeId, null);
			await WaitForWarningAsync(process, shouldExist: false);

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
