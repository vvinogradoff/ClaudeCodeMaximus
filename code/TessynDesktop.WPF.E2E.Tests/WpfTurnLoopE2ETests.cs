using System.Diagnostics;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the turn loop (W1a): the BUILT WPF app against a real loopback daemon (real <see cref="TdcpServer"/>, session module over real
/// session files, the tree) with a scripted turn API (<see cref="ScriptedTurnModule"/>) in place of the engine and the fake <c>claude</c>.
/// The prompt is typed and Send/Stop pressed through UI Automation; what the transcript page shows is read from the app's log (the page reports
/// each change of the live row, and each 1000 characters of streamed text, back to the host).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfTurnLoopE2ETests : IDisposable
{
	private const string InputName = "Prompt input";
	private const int SeedEntries = 6;

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-turn-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly ScriptedTurnModule _turns;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly string _main;

	public WpfTurnLoopE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, SeedEntries).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_main = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, TestEngines.Claude(new StubStreamingManager()), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		var catalog = new CatalogResult(
			[new ModelOptionDto("default", "Default", null), new ModelOptionDto("opus", "Opus 5.5", null)],
			["default", "high", "low"],
			[new ProfileOptionDto("default", "[Sys] Default"), new ProfileOptionDto("work", "Work")]);
		ClientTestHost? host = null;
		_turns = new ScriptedTurnModule(_env, () => host!.Bus);
		_host = host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree, new CatalogTestModule([], catalog), _turns]));

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
		_turns.ReleaseHeld();
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
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");   // the page is up: no message is queued behind its start
		Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task SendAsync(Process process, string text)
	{
		Assert.True(await _clicker.SetTextAsync(process, InputName, text, StepTimeout), "The prompt input was not found. Log:\n" + _app.ReadLog());
		Assert.True(await _clicker.ClickAsync(process, "Send", StepTimeout), "The Send button was not found. Log:\n" + _app.ReadLog());
	}

	private async Task ExpectAsync(Process process, string what, params string[] needles)
	{
		Assert.True(await _app.WaitForLogAsync(process, StepTimeout, needles), $"Expected: {what}. Log:\n{_app.ReadLog()}");
	}

	private static readonly System.Text.RegularExpressions.Regex RespondingChars = new(@"Live row shown by the page: Responding\.\.\., (\d+) chars", System.Text.RegularExpressions.RegexOptions.Compiled);
	private static readonly System.Text.RegularExpressions.Regex Stalls = new(@"UI thread stalled for (\d+) ms", System.Text.RegularExpressions.RegexOptions.Compiled);

	/// <summary>The characters of streamed text the page has reported so far (the most), 0 when none.</summary>
	private int ReportedChars() => RespondingChars.Matches(_app.ReadLog()).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();

	/// <summary>Waits until the page has reported at least <paramref name="atLeast"/> characters of streamed text. "At least", because on a loaded machine two chunks can merge into one page update.</summary>
	private async Task ExpectCharsAsync(Process process, int atLeast)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !process.HasExited && ReportedChars() < atLeast)
			await Task.Delay(100);

		Assert.True(ReportedChars() >= atLeast, $"Expected the page to report at least {atLeast} streamed characters, it reported {ReportedChars()}. Log:{Environment.NewLine}{_app.ReadLog()}");
	}

	/// <summary>Waits for a condition on the scripted daemon (instead of sleeping for a guessed time).</summary>
	private async Task WaitForAsync(string what, Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !condition())
			await Task.Delay(50);

		Assert.True(condition(), $"Expected: {what}. Log:{Environment.NewLine}{_app.ReadLog()}");
	}

	/// <summary>
	/// No error was logged, and the UI thread did not stall while the turn streamed (plan section 6). The stall monitor logs at 250 ms, which a loaded
	/// machine reaches now and then (the full test run is CPU heavy), so the check looks at what the monitor measured, after the first paint: no stall of
	/// a second or more and at most three shorter ones. Work done on the UI thread per delta would show as many and long stalls (12 page updates a second);
	/// a busy machine shows isolated, short ones.
	/// </summary>
	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);

		var afterStart = log[(log.IndexOf("click-to-first-paint", StringComparison.Ordinal))..];
		var stalls = Stalls.Matches(afterStart).Select(m => int.Parse(m.Groups[1].Value)).ToList();
		Assert.True(stalls.Count <= 3 && stalls.All(ms => ms < 1000), "UI thread stalls while streaming (ms): " + string.Join(", ", stalls));
	}

	[Fact]
	public async Task A_streamed_answer_appears_progressively_then_the_persisted_entry_replaces_it()
	{
		using var process = await StartAsync();
		try
		{
			await SendAsync(process, ScriptedTurnModule.StreamPrefix + " explain");

			await ExpectAsync(process, "the thinking row", "Live row shown by the page: Thinking...");
			await ExpectCharsAsync(process, ScriptedTurnModule.ChunkChars);
			Assert.DoesNotContain($", {SeedEntries + 2} in all", _app.ReadLog());   // the answer is not persisted yet: this is the preview
			await ExpectCharsAsync(process, 2 * ScriptedTurnModule.ChunkChars);
			Assert.True(ReportedChars() < ScriptedTurnModule.ChunkChars * ScriptedTurnModule.ChunkCount, "the whole answer arrived at once: not progressive");
			Assert.DoesNotContain($", {SeedEntries + 2} in all", _app.ReadLog());
			await ExpectAsync(process, "the persisted answer", $", {SeedEntries + 2} in all");
			await ExpectAsync(process, "the live row removed", "Live row shown by the page: , 0 chars");

			var send = Assert.Single(_turns.Sends);
			Assert.Equal(_main, send.NodeId);
			// The selectors show what the session was last used with, but the user picked nothing, so the turn carries no model, effort or profile
			// (tdcp-spec 9.8): the daemon resolves them and the session is not pinned to the restored values.
			Assert.Equal((null, null, null), (send.Model, send.Effort, send.Profile));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Stop_interrupts_a_running_turn_and_nothing_is_appended_for_the_answer()
	{
		using var process = await StartAsync();
		try
		{
			await SendAsync(process, ScriptedTurnModule.HoldPrefix + " wait");
			await ExpectAsync(process, "the running turn", "Live row shown by the page: Responding...");
			await ExpectAsync(process, "the prompt persisted", $", {SeedEntries + 1} in all");

			Assert.True(await _clicker.ClickAsync(process, "Stop", StepTimeout), "The Stop button was not found. Log:\n" + _app.ReadLog());

			await ExpectAsync(process, "the live row removed after the stop", "Live row shown by the page: , 0 chars");
			await WaitForAsync("the turn to end as stopped", () => _turns.Completions.Any(c => c.Cancelled));
			Assert.Equal(0, _turns.AnswersAppended);   // a stopped turn leaves no answer
			Assert.DoesNotContain($", {SeedEntries + 2} in all", _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_second_send_while_a_turn_runs_is_queued_and_runs_after_it()
	{
		using var process = await StartAsync();
		try
		{
			await SendAsync(process, ScriptedTurnModule.HoldPrefix + " first");
			await ExpectAsync(process, "the first turn running", "Live row shown by the page: Responding...");

			await SendAsync(process, "second quick");
			await ExpectAsync(process, "the second send shown as a queued row", "1 tool(s), 1 queued");

			_turns.ReleaseHeld();

			await ExpectAsync(process, "both answers persisted", $", {SeedEntries + 4} in all");
			Assert.Equal(["hold: first", "second quick"], _turns.Sends.Select(s => s.Text));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_turn_in_flight_survives_a_reconnect_and_shows_its_final_state()
	{
		using var process = await StartAsync();
		try
		{
			await SendAsync(process, ScriptedTurnModule.StreamPrefix + " across a drop");
			await ExpectCharsAsync(process, ScriptedTurnModule.ChunkChars);

			_host.StopServer();
			var published = _turns.ChunksPublished;
			await WaitForAsync("the turn to go on while the client is away", () => _turns.ChunksPublished >= published + 2);
			_host.StartServer();

			await ExpectAsync(process, "the persisted answer after the reconnect", $", {SeedEntries + 2} in all");
			await ExpectAsync(process, "the live row removed", "Live row shown by the page: , 0 chars");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
