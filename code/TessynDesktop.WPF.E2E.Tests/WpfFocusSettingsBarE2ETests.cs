using System.Diagnostics;
using System.Windows.Automation;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the settings bar of the Focus panel and the profile text of the closed Recent button (TC.DS.41, 43, 44, 46; TC.RD.07; FR.6.24, FR.1.33, UC.DS.14, UC.RD.03): the BUILT WPF app against a real
/// loopback daemon. Focus is tabbed over the Session view through the docking test hook, so a control found by name is the Focus panel's; the Session view's bar and gear are read after the Session
/// button brings it forward. The profile text of the Recent button is read through UI Automation inside the "Recent dropdown" button (the test daemon's state builder pins every session to a profile).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfFocusSettingsBarE2ETests : IDisposable
{
	private const string Session = "session";
	private const string Focus = "focus";
	private const string PinnedProfile = "Zeta profile";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-focus-settings-bar-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly RemoteSessionEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();

	public WpfFocusSettingsBarE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.Files.Set(_env.Session.FileName, [User("hello", 0), Assistant("hi", 1, "claude-opus-5-5", "high")]);

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Registry, _env.Streaming, _env.Attention, activity, profiles: new PinnedProfileResolver("work", PinnedProfile));
		var versions = new TreeVersionTracker();
		_env.Registry.TreeChanged += (_, e) =>
		{
			if (e.Kind != TreeChangeKind.DaemonMutation && e.Kind != TreeChangeKind.SessionIsRunningChanged)
				versions.Increment();
			else if (e.Kind == TreeChangeKind.DaemonMutation)
				_host!.Bus.Publish(RemoteProtocol.Events.TreeChanged, new TreeChangedPayload(versions.Current));
		};
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Settings), _env.Registry, states, activity, versions,
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		var mutation = new TreeMutationService(_env.Registry, _env.Settings, new FakeDirectoryLabelService(), new FakeDirectoryGitOrigins());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([
			new ClientRpcModule(runInfo: run, eventLog: log, treeMutation: mutation), _env.Module, tree, new TreeMutationRpcModule(mutation)]));

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

	private static string[] Dialogs(params string[] answers) => answers.SelectMany(a => new[] { "--test-dialog", a }).ToArray();

	private async Task<Process> StartAsync(params string[] arguments)
	{
		var process = _app.Start(new Dictionary<string, string>(), ["--test-monitors", "1", .. arguments]);
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", "Tree loaded:", $"Session {_env.Session.NodeId} opened");
		Assert.True(ready, "The app did not connect and open the session. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task PressAsync(Process process, string button) =>
		Assert.True(await _clicker.ClickAsync(process, button, StepTimeout), $"The '{button}' button was not found. Log:\n" + _app.ReadLog());

	private static async Task<bool> WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
				return true;

			await Task.Delay(200);
		}

		return false;
	}

	private bool HasSession()
	{
		lock (_env.Settings.SyncRoot)
			return _env.Directory.Sessions.Any(s => s.NodeId == _env.Session.NodeId);
	}

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	/// <summary>True when a window of the app (the main one or a floating panel's) holds an element with this name and control type.</summary>
	private static bool ExistsInAnyWindow(Process process, string name, ControlType type) =>
		new AppWindowFinder().FindAll(process.Id).Any(hwnd => AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
			new PropertyCondition(AutomationElement.ControlTypeProperty, type),
			new PropertyCondition(AutomationElement.NameProperty, name))) != null);

	private static async Task<WpfDockingHookClient> TabFocusOverSessionAsync(Process process)
	{
		var hook = await WpfDockingHookClient.ConnectAsync(process);
		Assert.True((await hook.TabAsync(Focus, Session)).Ok);
		await hook.BringForwardAsync(Focus);
		await hook.WaitStateAsync(r => r.Panels.Single(p => p.Id == Focus).Selected && !r.Panels.Single(p => p.Id == Session).Selected, "Focus tabbed over the Session view");
		return hook;
	}

	private static AutomationElement? FindIn(Process process, ControlType type, string name)
	{
		var hwnd = new AppWindowFinder().Find(process.Id);
		if (hwnd == IntPtr.Zero)
			return null;

		try
		{
			return AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.ControlTypeProperty, type), new PropertyCondition(AutomationElement.NameProperty, name)));
		}
		catch (ElementNotAvailableException)
		{
			return null;
		}
	}

	private static bool BarShown(Process process) =>
		new[] { "Model", "Effort", "Profile" }.All(n => FindIn(process, ControlType.ComboBox, n) != null);

	private static bool BarHidden(Process process) =>
		new[] { "Model", "Effort", "Profile" }.All(n => FindIn(process, ControlType.ComboBox, n) == null);

	private static ToggleState? GearState(Process process) =>
		FindIn(process, ControlType.Button, "Settings") is { } gear && gear.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern) ? ((TogglePattern)pattern).Current.ToggleState : null;

	[Fact]
	public async Task TC_DS_41_the_Focus_panel_shows_the_settings_bar_with_the_selectors_and_toggles_and_the_Settings_toggle_between_Send_answers_and_Session()
	{
		using var process = await StartAsync();
		try
		{
			using var hook = await TabFocusOverSessionAsync(process);
			Assert.True(await WaitForAsync(() => BarShown(process)), "The Focus panel shows no Model / Effort / Profile selectors. Log: " + _app.ReadLog());
			foreach (var name in new[] { "Streaming Session Mode", "Focus item sound", "Send answers", "Settings", "Session" })
				Assert.True(await _clicker.WaitForButtonAsync(process, name, StepTimeout), $"'{name}' was not found in the Focus panel. Log: " + _app.ReadLog());

			var send = _clicker.GetBounds(process, "Send answers");
			var settings = _clicker.GetBounds(process, "Settings");
			var session = _clicker.GetBounds(process, "Session");
			Assert.NotNull(send);
			Assert.NotNull(settings);
			Assert.NotNull(session);
			Assert.True(send.Value.Right <= settings.Value.Left + 1 && settings.Value.Right <= session.Value.Left + 1, "Order: Send answers (n), Settings, Session");
			Assert.Equal(ToggleState.On, GearState(process));

			var model = FindIn(process, ControlType.ComboBox, "Model")!.Current.BoundingRectangle;
			Assert.True(model.Top > send.Value.Bottom, "The bar is at the bottom of the panel, below the header");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task TC_DS_44_the_Settings_toggle_in_Focus_hides_and_shows_both_bars_and_the_composers_gear_shows_the_same_state()
	{
		using var process = await StartAsync();
		try
		{
			using var hook = await TabFocusOverSessionAsync(process);
			Assert.True(await WaitForAsync(() => BarShown(process)), "The Focus bar is not shown. Log: " + _app.ReadLog());

			Assert.True(await _clicker.ToggleAsync(process, "Settings", StepTimeout), "The Settings toggle of Focus was not found");
			Assert.True(await WaitForAsync(() => BarHidden(process)), "The Focus bar did not hide. Log: " + _app.ReadLog());
			Assert.Equal(ToggleState.Off, GearState(process));

			await PressAsync(process, "Session");
			await hook.WaitStateAsync(r => r.Panels.Single(p => p.Id == Session).Selected, "the Session view forward");
			Assert.True(await _clicker.WaitForButtonAsync(process, "Settings", StepTimeout), "The composer's gear was not found");
			Assert.True(await WaitForAsync(() => GearState(process) == ToggleState.Off), "The composer's gear does not show the flag the Focus toggle cleared");
			Assert.True(BarHidden(process), "The Session view's bar is still shown after Focus hid it");

			Assert.True(await _clicker.ToggleAsync(process, "Settings", StepTimeout));   // the composer's gear turns both on again
			Assert.True(await WaitForAsync(() => BarShown(process)), "The Session view's bar did not come back");
			await PressAsync(process, "Focus");
			await hook.WaitStateAsync(r => r.Panels.Single(p => p.Id == Focus).Selected, "Focus selected again");
			Assert.True(await WaitForAsync(() => BarShown(process) && GearState(process) == ToggleState.On), "The Focus bar and gear did not follow the composer's gear");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task TC_DS_46_the_Settings_toggle_and_the_bar_do_not_take_the_keyboard_focus_from_the_composer()
	{
		using var process = await StartAsync();
		try
		{
			using var hook = await TabFocusOverSessionAsync(process);
			Assert.True(await _clicker.WaitForButtonAsync(process, "Settings", StepTimeout));
			var gear = FindIn(process, ControlType.Button, "Settings")!;
			Assert.False(gear.Current.IsKeyboardFocusable, "The Settings toggle of Focus must not be keyboard focusable");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task TC_RD_07_the_closed_Recent_button_shows_the_open_sessions_profile_name_between_its_name_and_the_chevron()
	{
		using var process = await StartAsync();
		try
		{
			Assert.True(await WaitForAsync(() => FindIn(process, ControlType.Button, "Recent dropdown") is { } button
				&& button.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, PinnedProfile)) != null),
				"The closed Recent button does not show the profile text '" + PinnedProfile + "'. Log: " + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
