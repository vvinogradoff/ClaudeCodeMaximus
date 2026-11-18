using System.Diagnostics;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the active workers badge (FR.1.28, UC.AW.01, TC.AW.09): the BUILT WPF app against a real loopback daemon with the real tree module, the real registry and the real
/// <see cref="SessionStateBuilder"/>. A worker (and a worker of a worker) starting and stopping in the registry changes <c>runningWorkerCount</c> of the supervisor; the same
/// <c>session.changed</c> the daemon's publisher sends reaches the app, and the badge on the supervisor's row and on the closed Recent dropdown appears, counts and goes away.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfActiveWorkersE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-workers-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly RemoteSessionEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly SessionNodeModel _worker;
	private readonly SessionNodeModel _nested;

	public WpfActiveWorkersE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.Files.Set(_env.Session.FileName, [User("hello", 0), Assistant("hi", 1, "claude-opus-5-5", "high")]);
		_worker = _env.Registry.AddSession(_env.Directory, "Worker one");
		_worker.SupervisorNodeId = _env.Session.NodeId;
		_nested = _env.Registry.AddSession(_env.Directory, "Worker of worker");
		_nested.SupervisorNodeId = _worker.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Registry, _env.Streaming, _env.Attention, activity);
		var versions = new TreeVersionTracker();
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Settings), _env.Registry, states, activity, versions,
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), _env.Module, tree]));

		// What the daemon's publisher does when a worker starts or stops: session.changed for it and for every supervisor above it (the Core tests cover the publisher itself).
		_env.Registry.TreeChanged += (_, e) =>
		{
			if (e.Kind != TreeChangeKind.SessionIsRunningChanged || e.Session == null)
				return;

			var node = e.Session;
			var visited = new HashSet<string>();
			while (node != null && visited.Add(node.NodeId))
			{
				_host.Bus.Publish(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(states.Build(node)));
				node = node.SupervisorNodeId == null ? null : _env.Registry.FindModelByNodeId(node.SupervisorNodeId);
			}
		};

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

	private async Task<Process> StartAsync()
	{
		var process = _app.Start();
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", "Tree loaded:", $"Session {_env.Session.NodeId} opened");
		Assert.True(ready, "The app did not connect and load the tree. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task WaitForBadgeAsync(Process process, string text, bool shouldExist)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && _clicker.TextExists(process, text) != shouldExist)
			await Task.Delay(200);

		Assert.True(_clicker.TextExists(process, text) == shouldExist, $"The badge '{text}' {(shouldExist ? "never appeared" : "never went away")}. Log:\n" + _app.ReadLog());
	}

	[Fact]
	public async Task The_badge_appears_counts_direct_and_nested_workers_and_goes_away_at_zero()
	{
		using var process = await StartAsync();
		try
		{
			Assert.False(_clicker.TextExists(process, "Workers running now"), "No worker runs: no badge");

			_env.Registry.SetSessionRunning(_worker.NodeId, true);
			await WaitForBadgeAsync(process, "Workers running now: 1", shouldExist: true);

			_env.Registry.SetSessionRunning(_nested.NodeId, true);   // a worker of the worker counts for the supervisor too
			await WaitForBadgeAsync(process, "Workers running now: 2", shouldExist: true);

			_env.Registry.SetSessionRunning(_worker.NodeId, false);
			await WaitForBadgeAsync(process, "Workers running now: 1", shouldExist: true);

			_env.Registry.SetSessionRunning(_nested.NodeId, false);
			await WaitForBadgeAsync(process, "Workers running now", shouldExist: false);

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
