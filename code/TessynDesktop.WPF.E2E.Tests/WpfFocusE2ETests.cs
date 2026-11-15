using System.Diagnostics;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the Focus view with references and previews: the BUILT WPF app against a real loopback daemon (real
/// <see cref="TdcpServer"/>, the real session, attention and file modules over a temp registry, session files, content store and
/// attention ledger; <c>post_attention</c> is called through the real MCP server, so the items carry resolved references). The
/// buttons are pressed through UI Automation; what happened is read from the app's own log, including the lines the transcript
/// and preview pages report back (the quote was highlighted, the markdown was rendered).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfFocusE2ETests : IDisposable
{
	private const string TargetQuote = "42 ms for the indexed query";
	private const int TargetIndex = 21;

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-focus-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly SessionNodeModel _second;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly string _main;

	public WpfFocusE2ETests()
	{
		Directory.CreateDirectory(_root);

		// The main session has a long transcript; the second one holds the entry the Focus item points at.
		// The newest entry carries a link to a remote image (not on the allowlist) and a local path (a chip).
		var chipFile = _env.WriteFile("docs/chip.md", "# Chip file\n\nOpened from a chip.\n");
		_env.WriteBodies(Enumerable.Range(0, 60)
			.Select(i => i == 59 ? $"Chart: https://tracker.example/pixel.png?token=abc and https://allowed.example/auto.png and the notes are at {chipFile} done"
				: i % 2 == 0 ? $"question number {i}" : $"answer number {i} with **markdown**").ToArray());
		_second = _env.AddSession("Second session", Enumerable.Range(0, 30)
			.Select(i => i == TargetIndex ? $"The benchmark shows {TargetQuote} and 900 ms without it." : i % 2 == 0 ? $"other question {i}" : $"other answer {i}").ToArray());
		_main = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree, new CatalogTestModule(["allowed.example"])]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		var daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, daemonFile).Write(_host.Port);

		// Client state as a previous run left it: the directory expanded and the main session selected.
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

	private async Task PostAsync(JsonObject item)
	{
		var result = await _env.PostAsync(item);
		Assert.DoesNotContain("error", result.ToJsonString(), StringComparison.OrdinalIgnoreCase);
	}

	private async Task<Process> StartAndWaitForFocusAsync(int openItems)
	{
		var process = _app.Start();
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", $"Focus: {openItems} open item(s) for {_main}");
		Assert.True(ready, "The app did not open the main session with its Focus items. Log:\n" + _app.ReadLog());
		return process;
	}

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	[Fact]
	public async Task A_Focus_item_entry_reference_opens_the_other_session_at_the_quote_and_Back_returns()
	{
		await PostAsync(FocusRefsEnv.ItemWithRefs("Which approach?", FocusRefsEnv.EntryRef(TargetQuote, session: _second.NodeId, label: "the benchmark")));
		using var process = await StartAndWaitForFocusAsync(1);
		try
		{
			Assert.True(await _clicker.ClickAsync(process, "the benchmark", StepTimeout), "The reference button was not found. Log:\n" + _app.ReadLog());
			var jumped = await _app.WaitForLogAsync(process, StepTimeout, "Jumping to entry", $"Session {_second.NodeId} opened",
				$"Reference jump: entry {TargetIndex} visible true, quote highlighted true");
			Assert.True(jumped, "The jump did not land on the quote. Log:\n" + _app.ReadLog());
			Assert.Equal(1, _app.Count($"Session {_second.NodeId} opened"));   // the tree following the navigation must not open it a second time
			Assert.True(await _clicker.ClickAsync(process, "Back", StepTimeout), "The Back button was not found. Log:\n" + _app.ReadLog());

			var back = await _app.WaitForLogAsync(process, StepTimeout, $"Going back to {_main}");
			Assert.True(back, "Back did not run. Log:\n" + _app.ReadLog());
			Assert.True(await WaitForCountAsync(process, $"Session {_main} opened", 2), "The main session was not shown again. Log:\n" + _app.ReadLog());
			Assert.False(_clicker.Exists(process, "Back"), "Back stays visible with nothing left to go back to.");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_Focus_item_file_reference_opens_the_markdown_preview()
	{
		_env.WriteFile("docs/plan.md", "# The plan\n\n- step **one**\n- step two\n\nSee [the docs](https://example.com).\n");
		await PostAsync(FocusRefsEnv.ItemWithRefs("Approve the plan?", FocusRefsEnv.FileRef("docs/plan.md", label: "the plan")));
		using var process = await StartAndWaitForFocusAsync(1);
		try
		{
			Assert.True(await _clicker.ClickAsync(process, "the plan", StepTimeout), "The file reference button was not found. Log:\n" + _app.ReadLog());

			var shown = await _app.WaitForLogAsync(process, StepTimeout, "Preview opened: plan.md (\"Markdown\",", "Preview page rendered markdown: true");
			Assert.True(shown, "The markdown preview did not open and render. Log:\n" + _app.ReadLog());
			Assert.True(_clicker.Exists(process, "Open in default app"));           // a markdown file may be opened externally
			Assert.True(_clicker.Exists(process, "Close preview"));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task A_script_reference_is_shown_as_text_and_offers_no_external_open()
	{
		_env.WriteFile("tools/run.ps1", "Write-Host 'hello'\n");
		await PostAsync(FocusRefsEnv.ItemWithRefs("Run this script?", FocusRefsEnv.FileRef("tools/run.ps1", label: "the script")));
		using var process = await StartAndWaitForFocusAsync(1);
		try
		{
			Assert.True(await _clicker.ClickAsync(process, "the script", StepTimeout));

			var shown = await _app.WaitForLogAsync(process, StepTimeout, "Preview opened: run.ps1 (\"Text\",", "Preview page rendered text: true");
			Assert.True(shown, "The script was not shown as text. Log:\n" + _app.ReadLog());
			Assert.True(_clicker.Exists(process, "Close preview"));
			Assert.False(_clicker.Exists(process, "Open in default app"), "A script must never be offered for external opening.");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Transcript_previews_a_local_path_chip_and_loads_a_remote_image_only_after_a_click()
	{
		using var process = _app.Start();
		try
		{
			Assert.True(await _app.WaitForLogAsync(process, StartTimeout, $"Session {_main} opened"), "The session did not open. Log:\n" + _app.ReadLog());

			// An allowlisted host loads without a click, through the host's media proxy (the page only names https://media.local). The sandbox has no
			// network, so the proxy's fetch fails: the point is that the request reached the proxy at all. Remove the proxy or the request filter and
			// this line never appears (the request would be blocked as foreign instead).
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "Media proxy refused an image request"), "The allowlisted image never reached the media proxy. Log:\n" + _app.ReadLog());
			Assert.Equal(1, _app.Count("Media proxy refused an image request"));   // only the allowlisted host: the other URL is a button until clicked
			Assert.DoesNotContain("Blocked a request to a foreign host", _app.ReadLog());

			// Not on the allowlist: a "Load preview" button, and nothing was requested (the surface logs every blocked foreign request).
			Assert.True(await _clicker.ClickAsync(process, "Load preview tracker.example", StepTimeout), "The click-to-load button was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "Media load approved by a click for host tracker.example"), _app.ReadLog());
			Assert.DoesNotContain("Blocked a request to a foreign host", _app.ReadLog());

			// FR.26.5: the chip asks the daemon for a handle, then the preview pane shows the file.
			Assert.True(await _clicker.ClickAsync(process, "chip.md", StepTimeout), "The path chip was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, "Preview opened: chip.md (\"Markdown\",", "Preview page rendered markdown: true"), "The chip did not open the preview. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Send_answers_marks_the_item_answered_in_the_ledger()
	{
		await PostAsync(FocusRefsEnv.ItemWithRefs("Proceed with the migration?"));
		using var process = await StartAndWaitForFocusAsync(1);
		try
		{
			Assert.True(await _clicker.SetTextAsync(process, "Your answer", "Yes, go ahead", StepTimeout), "The reply box was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Send answers", StepTimeout), "Send answers was not available. Log:\n" + _app.ReadLog());

			Assert.True(await WaitForAsync(() => _env.Attention.GetItems(_main).Single().Status == AttentionStatus.Answered), "The item was not answered. Log:\n" + _app.ReadLog());
			Assert.Equal("Yes, go ahead", _env.Attention.GetItems(_main).Single().Answer);
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, $"Focus: 0 open item(s) for {_main}"), "The card did not leave the Focus view. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Dismiss_closes_the_item_in_the_ledger()
	{
		await PostAsync(new JsonObject { ["kind"] = "todo", ["title"] = "Check the logs", ["body"] = "later" });
		using var process = await StartAndWaitForFocusAsync(1);
		try
		{
			Assert.True(await _clicker.ClickAsync(process, "Dismiss", StepTimeout), "The Dismiss button was not found. Log:\n" + _app.ReadLog());

			Assert.True(await WaitForAsync(() => _env.Attention.GetItems(_main).Single().Status == AttentionStatus.Dismissed), "The item was not dismissed. Log:\n" + _app.ReadLog());
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, $"Focus: 0 open item(s) for {_main}"));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	private async Task<bool> WaitForCountAsync(Process process, string needle, int count)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			if (_app.Count(needle) >= count)
				return true;
			await Task.Delay(250);
		}

		return false;
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
}
