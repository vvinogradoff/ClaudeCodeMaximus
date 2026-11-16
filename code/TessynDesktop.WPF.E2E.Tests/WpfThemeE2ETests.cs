using System.Diagnostics;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the chrome of W10a: the BUILT WPF app starts with the stored theme (Dark), the title-bar theme button is pressed through UI Automation,
/// and the client settings file (<c>clients\wpf.json</c>, section <c>appearance</c>) must flip to Light; a second press flips it back.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfThemeE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-theme-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly string _main;
	private readonly string _settingsFile;

	public WpfThemeE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(["question", "answer"]);
		_main = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		_settingsFile = Path.Combine(clients.FullName, "wpf.json");
		File.WriteAllText(_settingsFile,
			$"{{\"lastSelectedNodeId\":\"{_main}\",\"expandedNodeIds\":[\"{_env.Base.Directory.NodeId}\"],\"sections\":{{\"appearance\":{{\"version\":1,\"data\":{{\"theme\":\"Dark\"}}}}}}}}");
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

	[Fact]
	public async Task Theme_button_flips_the_stored_theme_both_ways()
	{
		using var process = _app.Start();
		try
		{
			var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");
			Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());

			Assert.True(await _clicker.ClickAsync(process, "Toggle theme", StepTimeout), "The theme button was not found. Log:\n" + _app.ReadLog());
			await WaitForThemeAsync("Light");

			Assert.True(await _clicker.ClickAsync(process, "Toggle theme", StepTimeout));
			await WaitForThemeAsync("Dark");
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	private async Task WaitForThemeAsync(string theme)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		string text = string.Empty;
		while (DateTime.UtcNow < deadline)
		{
			try
			{
				text = File.ReadAllText(_settingsFile);
			}
			catch (IOException)
			{
				// The app is writing the file right now.
			}

			if (text.Contains($"\"theme\": \"{theme}\"", StringComparison.OrdinalIgnoreCase) || text.Contains($"\"theme\":\"{theme}\"", StringComparison.OrdinalIgnoreCase))
				return;

			await Task.Delay(100);
		}

		Assert.Fail($"Expected the stored theme to become {theme}. wpf.json:\n{text}\nLog:\n{_app.ReadLog()}");
	}
}
