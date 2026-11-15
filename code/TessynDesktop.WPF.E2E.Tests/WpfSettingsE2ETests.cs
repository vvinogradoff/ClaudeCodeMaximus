using System.Diagnostics;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the Settings window (W6a): the BUILT WPF app against a real loopback daemon whose <c>settings.*</c> methods are the real <c>SettingsRpcModule</c> over an in-memory section service.
/// The title-bar Settings button, the tabs, the text boxes and the Save button are driven through UI Automation. Covers a saved change that survives reopening the window, and a stale-version conflict that keeps
/// the user's edit. <b>Explicitly-run project:</b> it opens an off-screen window; do not run it while the Architect is working.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfSettingsE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-settings-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly RemoteSessionEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly ScriptedSettingsSections _sections = new();

	public WpfSettingsE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.Files.Set(_env.Session.FileName, [User("hello", 0), Assistant("hi", 1, "claude-opus-5-5", "high")]);

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
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([
			new ClientRpcModule(runInfo: run, eventLog: log, settingsSections: _sections), _env.Module, tree, new SettingsRpcModule(_sections)]));

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
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", "Tree loaded:");
		Assert.True(ready, "The app did not connect and load the tree. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task OpenNotificationsTabAsync(Process process)
	{
		Assert.True(await _clicker.ClickAsync(process, "Open Settings", StepTimeout), "The Settings button was not found. Log:\n" + _app.ReadLog());
		Assert.True(await _clicker.SelectTabAsync(process, "Notifications tab", StepTimeout), "The Notifications tab was not found. Log:\n" + _app.ReadLog());
		Assert.True(await _clicker.WaitForButtonAsync(process, "Reload Notifications", StepTimeout), "The Notifications tab did not load. Log:\n" + _app.ReadLog());
	}

	private async Task WaitForAsync(Func<bool> condition, string what)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !condition())
			await Task.Delay(100);

		Assert.True(condition(), $"{what}. Log:\n" + _app.ReadLog());
	}

	[Fact]
	public async Task AChangedValue_IsSaved_AndSurvivesClosingAndReopeningTheWindow()
	{
		using var process = await StartAsync();
		try
		{
			await OpenNotificationsTabAsync(process);
			Assert.True(await _clicker.SetTextAsync(process, "Sound throttle seconds", "42", StepTimeout));
			Assert.True(await _clicker.ClickAsync(process, "Save settings", StepTimeout));

			await WaitForAsync(() => _sections.Notifications.SessionCompletionSoundThrottleSeconds == 42, "The daemon did not get the new throttle");
			Assert.Equal(1, _sections.UpdateCount);

			// Close the window and open it again: the value comes back from the daemon.
			Assert.True(await _clicker.ClickAsync(process, "Close settings", StepTimeout));
			Assert.True(await _clicker.WaitForButtonGoneAsync(process, "Close settings", StepTimeout));
			await OpenNotificationsTabAsync(process);
			await WaitForAsync(() => _clicker.GetText(process, "Sound throttle seconds") == "42", "The reopened window did not show 42");
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task AStaleVersion_ShowsTheConflict_KeepsTheEdit_AndTheSecondSaveGoesThrough()
	{
		using var process = await StartAsync();
		try
		{
			await OpenNotificationsTabAsync(process);
			// Another client saves this section while the window is open: the version moves on.
			_sections.ChangeElsewhere(@"C:\Windows\Media\other.wav");
			Assert.True(await _clicker.SetTextAsync(process, "Sound throttle seconds", "77", StepTimeout));
			Assert.True(await _clicker.ClickAsync(process, "Save settings", StepTimeout));

			await WaitForAsync(() => _clicker.TextExists(process, "changed elsewhere"), "The conflict was not shown");
			Assert.Equal(0, _sections.UpdateCount);                                         // the stale write was refused
			Assert.Equal("77", _clicker.GetText(process, "Sound throttle seconds"));        // the edit is still there

			Assert.True(await _clicker.ClickAsync(process, "Save settings", StepTimeout));
			await WaitForAsync(() => _sections.Notifications.SessionCompletionSoundThrottleSeconds == 77, "The second save did not reach the daemon");
			Assert.Equal(@"C:\Windows\Media\other.wav", _sections.Notifications.SessionCompletionSoundFile);   // the other client's change was not overwritten
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
