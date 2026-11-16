using System.Diagnostics;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for WPF beside the Avalonia app: the BUILT WPF app against a real loopback daemon (the Avalonia app is the daemon's host in real life; here a test daemon plays it).
/// (1) WPF started first waits without a daemon and connects when one appears; (2) the daemon is replaced under the running app (the takeover: a new run on the same port with the same
/// token) and the app follows it; (3) the Startup tab's "Start the background daemon when none is running" is off by default and is stored in the client settings. The real host start
/// is off in these runs (a custom data root), so nothing here can launch a real daemon. <b>Explicitly-run project:</b> it opens an off-screen window.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfSideBySideE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-sbs-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly RemoteSessionEnv _env = new();
	private readonly UiAutomationClicker _clicker = new();
	private readonly ScriptedSettingsSections _sections = new();
	private readonly string _daemonFile;
	private readonly AppProcessHarness _app;
	private ClientTestHost? _host;

	public WpfSideBySideE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.Files.Set(_env.Session.FileName, [User("hello", 0), Assistant("hi", 1, "claude-opus-5-5", "high")]);
		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_env.Session.NodeId}\",\"expandedNodeIds\":[\"{_env.Directory.NodeId}\"]}}");
		_daemonFile = Path.Combine(_root, "daemon.json");
		_app = new AppProcessHarness(_root, _daemonFile);
	}

	public void Dispose()
	{
		_host?.Dispose();
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

	/// <summary>The daemon "Avalonia hosts": the real session/tree/settings modules on a loopback port, published in daemon.json.</summary>
	private ClientTestHost StartDaemon()
	{
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
		var host = new ClientTestHost((run, log) => new TdcpDispatcher([
			new ClientRpcModule(runInfo: run, eventLog: log, settingsSections: _sections), _env.Module, tree, new SettingsRpcModule(_sections)]));
		_host = host;
		PublishDaemonFile(host);
		return host;
	}

	private void PublishDaemonFile(ClientTestHost host)
	{
		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		new DaemonDiscoveryFile(host.RunInfo, tokens, _daemonFile).Write(host.Port);
	}

	private async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !condition())
			await Task.Delay(100);

		Assert.True(condition(), $"{what}. Log:\n" + _app.ReadLog());
	}

	[Fact]
	public async Task WpfStartedBeforeTheDaemon_KeepsWaiting_AndConnectsAsSoonAsTheDaemonComesUp()
	{
		using var process = _app.Start();
		try
		{
			Assert.False(File.Exists(_daemonFile));
			await Task.Delay(TimeSpan.FromSeconds(4));
			Assert.False(process.HasExited, "The app exited while waiting for the daemon. Log:\n" + _app.ReadLog());
			Assert.DoesNotContain("Connected to daemon run", _app.ReadLog());

			StartDaemon(); // Avalonia comes up (and publishes daemon.json)

			var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", "Tree loaded:");
			Assert.True(ready, "The app did not connect after the daemon appeared. Log:\n" + _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task WhenTheDaemonIsReplacedUnderTheRunningApp_TheAppFollowsTheNewRunByItself()
	{
		var host = StartDaemon();
		using var process = _app.Start();
		try
		{
			Assert.True(await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", "Tree loaded:"), "The app did not connect. Log:\n" + _app.ReadLog());
			var firstRun = host.RunInfo.RunId;
			var loads = _app.Count("Tree loaded:");

			// The takeover: the background host goes, Avalonia starts a new run on the same port with the same token and publishes its own daemon.json.
			host.StopServer();
			await Task.Delay(1500);
			host.RestartAsNewRun();
			PublishDaemonFile(host);
			Assert.NotEqual(firstRun, host.RunInfo.RunId);

			await WaitForAsync(() => _app.Count("Tree loaded:") > loads, "The app did not reload the tree from the new daemon run");
			Assert.False(process.HasExited, "The app went away with the old daemon");
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task TheStartupSetting_IsOffByDefault_AndSavingItStoresItInTheClientSettings()
	{
		StartDaemon();
		using var process = _app.Start();
		try
		{
			Assert.True(await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", "Tree loaded:"), "The app did not connect. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Open Settings", StepTimeout), "The Settings button was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.SelectTabAsync(process, "Startup tab", StepTimeout), "The Startup tab was not found. Log:\n" + _app.ReadLog());

			await WaitForAsync(() => _clicker.TextExists(process, "Takes effect at once"), "The Startup tab did not show");
			Assert.False(_clicker.IsCheckBoxChecked(process, "Start the background daemon"), "The setting must be off by default");

			Assert.True(await _clicker.ToggleCheckBoxAsync(process, "Start the background daemon", StepTimeout));
			Assert.True(await _clicker.ClickAsync(process, "Save settings", StepTimeout));

			var file = Path.Combine(_root, "clients", "wpf.json");
			await WaitForAsync(() => File.Exists(file) && File.ReadAllText(file).Contains("startBackgroundDaemon\":true", StringComparison.OrdinalIgnoreCase)
				|| File.Exists(file) && File.ReadAllText(file).Replace(" ", "").Contains("\"startBackgroundDaemon\":true", StringComparison.OrdinalIgnoreCase),
				"The client settings file did not get the setting");
			Assert.Contains("daemonHost", File.ReadAllText(file));
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
