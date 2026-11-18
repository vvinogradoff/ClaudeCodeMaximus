using System.Diagnostics;
using System.Windows.Automation;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Contract;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the composer completion (W1b): the BUILT WPF app against a real loopback daemon with the real draft and autocomplete modules, the
/// real presence registry and acknowledgement book. A suggestion is chosen through UI Automation (the rows are buttons), the draft survives an app
/// restart, the sign-in notice follows <c>session.changed</c>, and the Focus item sound (FR.22) is played by the client (a test window only logs it) and
/// acknowledged to the daemon.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfComposerCompletionE2ETests : IDisposable
{
	private const string InputName = "Prompt input";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-completion-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly DraftService _drafts;
	private readonly ClientPresenceRegistry _presence = new();
	private readonly NotificationAckBook _acks = new();
	private readonly SessionAuthErrorTracker _authErrors = new();
	private readonly FakeCodeIndexService _codeIndex = new();
	private readonly AutocompleteRpcModule _autocomplete;
	private readonly string _main;

	public WpfComposerCompletionE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, 4).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_main = _env.Session.NodeId;

		_drafts = new DraftService(Path.Combine(_root, "drafts"));
		_codeIndex.SetReady();
		_codeIndex.Symbols =
		[
			new CodeSymbolModel { Name = "Foo", FullyQualifiedName = "Foo", Namespace = "My.Space", Kind = CodeSymbolKind.Class, FilePath = "Foo.cs", Line = 3 },
			new CodeSymbolModel { Name = "FooBar", FullyQualifiedName = "FooBar", Namespace = "My.Space", Kind = CodeSymbolKind.Class, FilePath = "FooBar.cs", Line = 9 },
		];
		_autocomplete = new AutocompleteRpcModule(_env.Base.Registry, _codeIndex);
		_env.Base.Settings.Settings.SessionCompletionSoundFile = Path.Combine(_root, "done.wav");

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity, _authErrors);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		var settings = new SettingsRpcModule(new SettingsSectionService(_env.Base.Settings, [new NotificationsSettingsSection()]));
		_host = new ClientTestHost((run, log) => new TdcpDispatcher(
		[
			new ClientRpcModule(runInfo: run, eventLog: log, drafts: _drafts, codeIndex: _codeIndex, authErrors: _authErrors, presence: _presence, acks: _acks),
			.. _env.Modules, tree, new CatalogTestModule([]), new DraftRpcModule(_env.Base.Registry, _drafts), _autocomplete, settings,
		]), presence: _presence);

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
		_autocomplete.Dispose();
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
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened");
		Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task WaitUntilAsync(Func<bool> condition, string what)
	{
		// The condition may consume what it checks (an acknowledgement), so it is evaluated once per round and its last answer is the verdict.
		var deadline = DateTime.UtcNow + StepTimeout;
		var reached = condition();
		while (!reached && DateTime.UtcNow < deadline)
		{
			await Task.Delay(200);
			reached = condition();
		}

		Assert.True(reached, $"Not reached: {what}. Log:\n{_app.ReadLog()}");
	}

	private async Task<string> WaitForInputAsync(Process process, Func<string, bool> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		string? text = null;
		while (DateTime.UtcNow < deadline)
		{
			text = _clicker.GetText(process, InputName);
			if (text != null && condition(text))
				return text;

			await Task.Delay(250);
		}

		Assert.Fail($"The prompt input never reached the expected text; it shows '{text}'. Log:\n" + _app.ReadLog());
		return string.Empty;
	}

	private string? SavedDraft() => _drafts.LoadDraft(_env.Session.FileName);

	// ---- autocomplete ----

	[Fact]
	public async Task Typing_a_hash_offers_suggestions_and_choosing_one_replaces_the_trigger_text()
	{
		using var process = await StartAsync();
		try
		{
			Assert.True(await _clicker.SetTextAsync(process, InputName, "see #Fo", StepTimeout));
			Assert.True(await _clicker.PlaceCaretAtEndAsync(process, InputName, StepTimeout), "The caret could not be placed. Log:\n" + _app.ReadLog());

			Assert.True(await _clicker.WaitForButtonAsync(process, "FooBar", StepTimeout), "The suggestions did not appear. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Foo", StepTimeout));

			var text = await WaitForInputAsync(process, t => t.Contains("My.Space.Foo", StringComparison.Ordinal));
			Assert.Equal("see My.Space.Foo", text);
			Assert.True(await _clicker.WaitForButtonGoneAsync(process, "FooBar", StepTimeout), "The popup stayed open.");
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_drive_path_is_offered_from_the_real_directory_and_a_folder_continues_the_path()
	{
		var sub = Directory.CreateDirectory(Path.Combine(_root, "pathdir", "alpha-folder")).FullName;
		File.WriteAllText(Path.Combine(_root, "pathdir", "alpha-file.txt"), "x");
		var typed = "open " + Path.Combine(_root, "pathdir") + Path.DirectorySeparatorChar + "alpha";
		using var process = await StartAsync();
		try
		{
			Assert.True(await _clicker.SetTextAsync(process, InputName, typed, StepTimeout));
			Assert.True(await _clicker.PlaceCaretAtEndAsync(process, InputName, StepTimeout));

			Assert.True(await _clicker.WaitForButtonAsync(process, "alpha-file.txt", StepTimeout), "The path suggestions did not appear. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "alpha-folder", StepTimeout));

			var text = await WaitForInputAsync(process, t => t.EndsWith(Path.DirectorySeparatorChar));
			Assert.Equal("open " + sub + Path.DirectorySeparatorChar, text);
		}
		finally
		{
			_app.Stop(process);
		}
	}

	// ---- drafts ----

	[Fact]
	public async Task A_typed_draft_reaches_the_daemon_and_is_restored_after_a_restart()
	{
		using (var first = await StartAsync())
		{
			Assert.True(await _clicker.SetTextAsync(first, InputName, "half-written thought", StepTimeout));
			await WaitUntilAsync(() => SavedDraft() == "half-written thought", "the draft was written to the daemon");
			_app.Stop(first);
		}

		using var second = await StartAsync();
		try
		{
			var text = await WaitForInputAsync(second, t => t == "half-written thought");
			Assert.Equal("half-written thought", text);
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(second);
		}
	}

	[Fact]
	public async Task Text_typed_just_before_closing_is_still_saved()
	{
		using (var process = await StartAsync())
		{
			Assert.True(await _clicker.SetTextAsync(process, InputName, "typed at the last moment", StepTimeout));
			_app.Stop(process);   // closes at once: the debounce has not fired, the exit flush must write it
		}

		await WaitUntilAsync(() => SavedDraft() == "typed at the last moment", "the exit flush wrote the draft");
	}

	[Fact]
	public async Task A_draft_written_by_another_client_while_the_session_is_open_appears_in_the_input()
	{
		using var process = await StartAsync();
		try
		{
			_drafts.SaveDraft(_env.Session.FileName, "from the desktop composer");
			_host.Bus.Publish(RemoteProtocol.Composer.DraftChangedEvent, new DraftChangedPayload(_main, "someone-else"));

			var text = await WaitForInputAsync(process, t => t == "from the desktop composer");
			Assert.Equal("from the desktop composer", text);
		}
		finally
		{
			_app.Stop(process);
		}
	}

	// ---- auth error ----

	[Fact]
	public async Task The_sign_in_notice_appears_with_the_auth_error_flag_and_goes_when_the_flag_goes()
	{
		using var process = await StartAsync();
		try
		{
			Assert.False(_clicker.Exists(process, "Re-authenticate"));

			_host.Bus.Publish(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State(authError: true)));
			Assert.True(await _clicker.WaitForButtonAsync(process, "Re-authenticate", StepTimeout), "The notice did not appear. Log:\n" + _app.ReadLog());
			Assert.True(_clicker.TextExists(process, "not signed in"));

			_host.Bus.Publish(RemoteProtocol.Events.SessionChanged, new SessionChangedPayload(State(authError: false)));
			Assert.True(await _clicker.WaitForButtonGoneAsync(process, "Re-authenticate", StepTimeout), "The notice stayed.");
		}
		finally
		{
			_app.Stop(process);
		}
	}

	private SessionStateDto State(bool authError) => new(_main, false, false, null, false, null, null, false, null, authError);

	// ---- Focus item sound ----

	[Fact]
	public async Task The_client_registers_the_sound_channel_plays_the_focus_item_sound_and_acknowledges_it()
	{
		using var process = await StartAsync();
		try
		{
			await WaitUntilAsync(() => _presence.IsPresented(RemoteProtocol.Notifications.SoundChannel), "the sound channel was registered");
			Assert.False(_presence.IsPresented(RemoteProtocol.Notifications.ToastChannel));   // toasts belong to the notification package

			_host.Bus.Publish(RemoteProtocol.Notifications.FocusItemCreatedEvent, new FocusItemCreatedPayload(_main, "A1", "ask", DateTimeOffset.UtcNow));

			var since = DateTimeOffset.UtcNow.AddMinutes(-1);
			await WaitUntilAsync(() => _acks.TryConsumeSoundAck(_main, since), "the Focus item sound was acknowledged");
			Assert.Contains("Focus item sound (silent in a test window)", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_completed_turn_makes_no_sound()
	{
		using var process = await StartAsync();
		try
		{
			await WaitUntilAsync(() => _presence.IsPresented(RemoteProtocol.Notifications.SoundChannel), "the sound channel was registered");

			_host.Bus.Publish(RemoteProtocol.Events.TurnCompleted, new TurnCompletedPayload(_main, false, null, "t1"));
			_host.Bus.Publish(RemoteProtocol.Events.TurnCompleted, new TurnCompletedPayload(_main, true, null, "t2"));
			await Task.Delay(1500);

			Assert.DoesNotContain("Focus item sound", _app.ReadLog());
			Assert.False(_acks.TryConsumeSoundAck(_main, DateTimeOffset.UtcNow.AddMinutes(-1)));
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
