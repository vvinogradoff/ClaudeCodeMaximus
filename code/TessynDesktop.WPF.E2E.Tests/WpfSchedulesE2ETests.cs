using System.Diagnostics;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the schedules UI (W8, FR.14): the BUILT WPF app against a real loopback daemon with the real <c>schedules.*</c> methods (real scheduler and API service over the
/// test session registry). The prompt is typed, "Send later" pressed (the dialog answered by the test-window script) and the indicator and its Cancel button are driven through UI
/// Automation; what happened is read from the daemon's scheduler.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfSchedulesE2ETests : IDisposable
{
	private const string InputName = "Prompt input";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-schedules-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly SchedulerService _scheduler;
	private readonly ScheduleApiService _api;
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly UiAutomationTree _rows = new();
	private readonly string _main;

	public WpfSchedulesE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_main = _env.Session.NodeId;
		_scheduler = new SchedulerService(_env.Base.Settings, new FakeTurnService(), _env.Base.Registry);   // never started: nothing fires during a test
		_api = new ScheduleApiService(_scheduler, _env.Base.Registry, TimeProvider.System);

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity, scheduler: _scheduler);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log, schedules: _api), .. _env.Modules, tree, new ScheduleRpcModule(_api), new CatalogTestModule([])]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_main}\",\"expandedNodeIds\":[\"{_env.Base.Directory.NodeId}\"]}}");
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
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");
		Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());
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

	private List<ScheduleModel> SchedulesOfMain() => [.. _scheduler.GetSchedules().Where(s => s.TargetNodeId == _main)];

	[Fact]
	public async Task A_delayed_send_is_scheduled_shows_the_indicator_and_Cancel_removes_it()
	{
		using var process = await StartAsync("--test-dialog", "text:2m");
		try
		{
			Assert.True(await _clicker.SetTextAsync(process, InputName, "remind me tomorrow", StepTimeout), "The prompt input was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Send later", StepTimeout), "The Send later button was not found. Log:\n" + _app.ReadLog());

			Assert.True(await WaitForAsync(() => SchedulesOfMain().Count == 1), "The daemon got no schedule. Log:\n" + _app.ReadLog());
			var schedule = SchedulesOfMain().Single();
			Assert.Equal("remind me tomorrow", schedule.Prompt);
			Assert.Equal(ScheduleKind.Delay, schedule.Kind);
			Assert.InRange((schedule.FireAtUtc!.Value - DateTimeOffset.UtcNow).TotalSeconds, 60, 125);   // about two minutes ahead
			Assert.True(await WaitForAsync(() => string.IsNullOrEmpty(_clicker.GetText(process, InputName))), "The prompt input was not emptied.");

			Assert.True(await _clicker.WaitForButtonAsync(process, "Schedules", StepTimeout), "The indicator did not appear. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ToggleAsync(process, "Schedules", StepTimeout), "The indicator could not be opened.");
			Assert.True(await _clicker.WaitForButtonAsync(process, "Cancel schedule " + schedule.ScheduleId, StepTimeout), "The schedule's Cancel button was not found. Log:\n" + _app.ReadLog());

			Assert.True(await _clicker.ClickAsync(process, "Cancel schedule " + schedule.ScheduleId, StepTimeout));

			Assert.True(await WaitForAsync(() => SchedulesOfMain().Count == 0), "The schedule was not removed from the daemon.");
			Assert.True(await _clicker.WaitForButtonGoneAsync(process, "Schedules", StepTimeout), "The indicator did not go away. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_schedule_made_elsewhere_shows_up_on_schedule_changed()
	{
		using var process = await StartAsync();
		try
		{
			Assert.False(_clicker.Exists(process, "Schedules"));

			var added = _api.Add(new ScheduleAddRequestModel(_main, "from the mcp tool", "delay", DateTimeOffset.UtcNow.AddMinutes(10), null, null, null, null));
			_host.Bus.Publish(RemoteProtocol.Schedules.ChangedEvent, new ScheduleChangedPayload(_main));

			Assert.True(await _clicker.WaitForButtonAsync(process, "Schedules", StepTimeout), "The indicator did not appear. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ToggleAsync(process, "Schedules", StepTimeout));
			Assert.True(await _clicker.WaitForButtonAsync(process, "Cancel schedule " + added.Id, StepTimeout), "The new schedule is not listed. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_session_with_a_schedule_carries_the_clock_on_its_tree_row()
	{
		_api.Add(new ScheduleAddRequestModel(_main, "later", "delay", DateTimeOffset.UtcNow.AddMinutes(10), null, null, null, null));

		using var process = await StartAsync();
		try
		{
			Assert.True(await WaitForAsync(() => _clicker.TextExists(process, "Schedule indicator")), "The tree row has no schedule indicator. Log:\n" + _app.ReadLog());
			Assert.True(await _rows.WaitForRowAsync(process, _env.Session.Name, shouldExist: true, StepTimeout));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
