using System.Diagnostics;
using System.Text.Json;
using System.Windows.Automation;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the session tag area (FR.24 mini/full): the BUILT WPF app against a real loopback daemon whose session carries many tags.
/// The area is expanded and collapsed through UI Automation (its ExpandCollapse pattern; the mouse gestures are covered with real layout by
/// <c>SessionTagAreaViewTests</c>), and the mode is read back from the client state file: it is not remembered (an old "tagAreaExpanded": true is ignored, a restart starts collapsed).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfTagAreaE2ETests : IDisposable
{
	private const string AreaId = "SessionTagArea";
	private const int TagCount = 40;

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-tagarea-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationExpander _area = new();
	private readonly string _main;

	public WpfTagAreaE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_main = _env.Session.NodeId;
		_env.Base.TagService.Tags.AddRange(Enumerable.Range(0, TagCount).Select(i => new SessionTagModel
		{
			Kind = SessionTagKind.GitHubPullRequest,
			Key = $"owner/repo#{i}",
			Label = $"repo PR #{1000 + i}",
			Url = $"https://github.com/owner/repo/pull/{1000 + i}",
		}));

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree, new CatalogTestModule([])]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);
		_app = new AppProcessHarness(_root, daemonFile);

		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_main}\",\"tagAreaExpanded\":true,\"expandedNodeIds\":[\"{_env.Base.Directory.NodeId}\"]}}");   // the old remembered mode must be ignored
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

	private string StateFile => Path.Combine(_root, "clients", "wpf.json");

	private bool? StoredMode()
	{
		using var document = JsonDocument.Parse(File.ReadAllText(StateFile));
		return document.RootElement.TryGetProperty("tagAreaExpanded", out var value) ? value.GetBoolean() : null;
	}

	private async Task<Process> StartAsync()
	{
		var process = _app.Start();
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened");
		Assert.True(ready, "The session did not open. Log:\n" + _app.ReadLog());
		return process;
	}

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	[Fact]
	public async Task The_tag_area_starts_collapsed_expands_and_collapses_through_automation_and_is_not_remembered_across_a_restart()
	{
		using (var first = await StartAsync())
		{
			try
			{
				Assert.True(await _area.WaitForStateAsync(first, AreaId, ExpandCollapseState.Collapsed, StepTimeout), "The tag area did not show up in mini mode (the old stored full mode must be ignored). Log:\n" + _app.ReadLog());
				var mini = _area.GetHeight(first, AreaId) ?? 0;
				Assert.True(mini > 0);

				Assert.True(_area.TrySet(first, AreaId, expand: true));
				Assert.True(await _area.WaitForStateAsync(first, AreaId, ExpandCollapseState.Expanded, StepTimeout));
				Assert.True((_area.GetHeight(first, AreaId) ?? 0) > mini * 2, "full mode must show several lines of chips");

				Assert.True(_area.TrySet(first, AreaId, expand: false));
				Assert.True(await _area.WaitForStateAsync(first, AreaId, ExpandCollapseState.Collapsed, StepTimeout));
				Assert.Equal(mini, _area.GetHeight(first, AreaId) ?? 0, 1);

				Assert.True(_area.TrySet(first, AreaId, expand: true));
				Assert.True(await _area.WaitForStateAsync(first, AreaId, ExpandCollapseState.Expanded, StepTimeout));
				AssertNoErrors();
			}
			finally
			{
				_app.Stop(first);
			}
		}

		// The first run ended expanded, but nothing was written: a second run opens the session collapsed again.
		Assert.Null(StoredMode());

		using var second = await StartAsync();
		try
		{
			Assert.True(await _area.WaitForStateAsync(second, AreaId, ExpandCollapseState.Collapsed, StepTimeout), "The area did not start collapsed. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(second);
		}

		Assert.Null(StoredMode());
	}

	[Fact]
	public async Task A_stored_tag_cloud_font_size_makes_the_mini_line_follow_the_chip_size()
	{
		double defaultMini;
		using (var baseline = await StartAsync())
		{
			try
			{
				Assert.True(await _area.WaitForStateAsync(baseline, AreaId, ExpandCollapseState.Collapsed, StepTimeout), "Log:\n" + _app.ReadLog());
				defaultMini = _area.GetHeight(baseline, AreaId) ?? 0;
				Assert.True(defaultMini > 0);
			}
			finally
			{
				_app.Stop(baseline);
			}
		}

		// The same session with a much bigger tag cloud size in the appearance section: the one mini line must be taller.
		File.WriteAllText(StateFile,
			$"{{\"lastSelectedNodeId\":\"{_main}\",\"expandedNodeIds\":[\"{_env.Base.Directory.NodeId}\"],\"sections\":{{\"appearance\":{{\"version\":1,\"data\":{{\"tagFontSize\":28}}}}}}}}");
		using var big = await StartAsync();
		try
		{
			Assert.True(await _area.WaitForStateAsync(big, AreaId, ExpandCollapseState.Collapsed, StepTimeout), "Log:\n" + _app.ReadLog());
			var bigMini = _area.GetHeight(big, AreaId) ?? 0;
			Assert.True(bigMini > defaultMini + 8, $"mini line {bigMini} did not grow from {defaultMini} with a 28 pt tag cloud");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(big);
		}
	}}
