using System.Diagnostics;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the Recent tab and the favourites (W3): the BUILT WPF app against a real loopback daemon with the real session module (so
/// <c>session.setFavorite</c> really flips the node) and a test <c>recent.list</c>. The tab, the rows and the stars are driven through UI
/// Automation; what happened is read from the app's own log and from the daemon's node objects.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfRecentE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-recent-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly SessionNodeModel _second;
	private readonly RecentTestModule _recent;
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();

	public WpfRecentE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_second = _env.AddSession("Second session", Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? $"other question {i}" : $"other answer {i}").ToArray());
		_recent = new RecentTestModule([_env.Session, _second]);

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, TestEngines.Claude(new StubStreamingManager()), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree, _recent, new CatalogTestModule([])]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);
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
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run");
		Assert.True(ready, "The app did not connect. Log:\n" + _app.ReadLog());
		return process;
	}

	private static async Task<bool> WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
				return true;
			await Task.Delay(250);
		}

		return false;
	}

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	[Fact]
	public async Task The_Recent_tab_lists_the_sessions_by_directory_and_a_click_opens_one()
	{
		using var process = await StartAsync();
		try
		{
			Assert.True(await _clicker.ClickMainMenuItemAsync(process, "Recent", StepTimeout), "The Recent tab was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.WaitForButtonAsync(process, "Open " + _second.Name, StepTimeout), "The Recent rows did not appear. Log:\n" + _app.ReadLog());
			Assert.True(_clicker.Exists(process, "Open " + _env.Session.Name));
			Assert.True(_clicker.Exists(process, "Group " + _env.Base.Directory.Path), "The directory header was not found.");

			Assert.True(await _clicker.ClickAsync(process, "Open " + _second.Name, StepTimeout));

			var opened = await _app.WaitForLogAsync(process, StepTimeout, $"Session {_second.NodeId} opened");
			Assert.True(opened, "The session was not opened from the Recent tab. Log:\n" + _app.ReadLog());
			Assert.Equal(1, _app.Count($"Session {_second.NodeId} opened"));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_favourite_toggled_from_the_list_and_from_the_session_header_reaches_the_daemon()
	{
		using var process = await StartAsync();
		try
		{
			Assert.True(await _clicker.ClickMainMenuItemAsync(process, "Recent", StepTimeout), "The Recent tab was not found.");
			Assert.True(await _clicker.ClickAsync(process, "Favourite " + _second.Name, StepTimeout), "The star of the row was not found. Log:\n" + _app.ReadLog());
			Assert.True(await WaitForAsync(() => _second.IsFavorite), "The daemon did not get the favourite flag.");

			Assert.True(await _clicker.ClickAsync(process, "Open " + _second.Name, StepTimeout));
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, $"Session {_second.NodeId} opened"));
			Assert.True(await _clicker.ClickAsync(process, "Favourite", StepTimeout), "The header star was not found. Log:\n" + _app.ReadLog());

			Assert.True(await WaitForAsync(() => !_second.IsFavorite), "The header star did not clear the favourite flag.");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Hiding_a_row_in_the_Recent_dropdown_removes_it_and_tells_the_daemon()
	{
		using var process = await StartAsync();
		try
		{
			Assert.True(await _clicker.ToggleAsync(process, "Recent dropdown", StepTimeout), "The dropdown button was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Hide " + _env.Session.Name, StepTimeout), "The hide button was not found in the dropdown. Log:\n" + _app.ReadLog());

			Assert.True(await _clicker.WaitForButtonGoneAsync(process, "Open " + _env.Session.Name, StepTimeout), "The hidden row is still listed.");
			Assert.Contains(_env.Session.NodeId, _recent.Hidden);
			Assert.True(_clicker.Exists(process, "Open " + _second.Name));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
