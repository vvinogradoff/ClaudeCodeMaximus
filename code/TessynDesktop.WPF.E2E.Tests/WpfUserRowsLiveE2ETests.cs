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
public sealed class WpfUserRowsLiveE2ETests : IDisposable
{
	private const string InputName = "Prompt input";
	private const int SeedEntries = 6;

	private int _cdpPort;
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-userlive-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly ScriptedTurnModule _turns;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly string _main;

	public WpfUserRowsLiveE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, SeedEntries).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_main = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity);
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
		var port = CdpPage.FreePort();
		_cdpPort = port;
		var process = _app.Start(CdpPage.EnvironmentFor(port));
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
		Assert.True(await _app.WaitForLogAsync(process, StepTimeout, needles), $"Expected: {what}. Log:{Environment.NewLine}{_app.ReadLog()}");
	}

	[Fact]
	public async Task A_queued_input_that_runs_appears_as_a_visible_user_row()
	{
		using var process = await StartAsync();
		using var page = await CdpPage.ConnectAsync(_cdpPort, "transcript.html", StartTimeout);
		try
		{
			await SendAsync(process, ScriptedTurnModule.HoldPrefix + " first");
			await ExpectAsync(process, "the first turn running", "Live row shown by the page: Responding...");
			await SendAsync(process, "second quick");
			await ExpectAsync(process, "the second send shown as a queued row", "1 tool(s), 1 queued");
			_turns.ReleaseHeld();
			await ExpectAsync(process, "both answers persisted", $", {SeedEntries + 4} in all");

			var deadline = DateTime.UtcNow + StepTimeout;
			string? found = null;
			while (DateTime.UtcNow < deadline)
			{
				found = (await page.EvalAsync(UserRowProbe("second quick")))?.ToString();
				if (found != null && found != "null")
					break;
				await Task.Delay(200);
			}

			Assert.True(found != null && found != "null", "The sent input never appeared as a user row. Log:" + Environment.NewLine + _app.ReadLog());
			var o = System.Text.Json.Nodes.JsonNode.Parse(found!)!.AsObject();
			Assert.True(o["w"]!.GetValue<double>() > 100 && o["h"]!.GetValue<double>() > 15, "the user row has no size: " + found);
			Assert.True(o["contrast"]!.GetValue<double>() >= 4.5, "the user row text has no contrast with its bubble: " + found);
			Assert.True(o["inView"]!.GetValue<bool>(), "the user row is outside the view: " + found);
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task An_input_sent_after_the_session_file_was_rewritten_underneath_the_open_page_is_shown()
	{
		using var process = await StartAsync();
		using var page = await CdpPage.ConnectAsync(_cdpPort, "transcript.html", StartTimeout);
		try
		{
			// What the duplicate cleanup does: the file shrinks (entries dropped) while the session is open.
			_env.WriteBodies("question 0", "answer 1", "question 2", "answer 3");
			await SendAsync(process, "after the rewrite");

			var deadline = DateTime.UtcNow + StepTimeout;
			string? found = null;
			while (DateTime.UtcNow < deadline)
			{
				found = (await page.EvalAsync(UserRowProbe("after the rewrite")))?.ToString();
				if (found != null && found != "null")
					break;
				await Task.Delay(200);
			}

			Assert.True(found != null && found != "null", "PAGE " + await page.EvalAsync("JSON.stringify(S.items.map(i => i.i + i.r + ':' + i.html.substring(0, 30)))") + " The sent input never appeared as a user row. Log:" + Environment.NewLine + _app.ReadLog());
			var o = System.Text.Json.Nodes.JsonNode.Parse(found!)!.AsObject();
			Assert.True(o["w"]!.GetValue<double>() > 100 && o["h"]!.GetValue<double>() > 15, "the user row has no size: " + found);
			Assert.True(o["contrast"]!.GetValue<double>() >= 4.5, "no contrast: " + found);
		}
		finally
		{
			_app.Stop(process);
		}
	}

	/// <summary>Script: the user row whose text contains <paramref name="text"/> as JSON { w, h, contrast, inView }, or null when there is none.</summary>
	private static string UserRowProbe(string text) => @"(() => {
		function parse(c) { const srgb = c.startsWith('color(srgb'), m = c.match(/[\d.]+/g).map(Number), k = srgb ? 255 : 1; return { r: m[0] * k, g: m[1] * k, b: m[2] * k, a: m.length > 3 ? m[3] : 1 }; }
		function lum(c) { const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b); }
		const row = Array.from(document.querySelectorAll('.e.U')).find(e => e.textContent.includes(" + "\"" + text + "\"" + @"));
		if (!row) return null;
		const c = row.querySelector('.c'), b = row.querySelector('.b'), r = c.getBoundingClientRect(), l1 = lum(parse(getComputedStyle(b).color)), l2 = lum(parse(getComputedStyle(c).backgroundColor));
		return JSON.stringify({ w: r.width, h: r.height, contrast: (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05), inView: r.bottom > 0 && r.top < innerHeight });
	})()";
}
