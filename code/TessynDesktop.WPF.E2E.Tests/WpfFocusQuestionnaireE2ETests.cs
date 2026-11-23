using System.Diagnostics;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the Focus questionnaire (FR.23.33 to FR.23.37, UC.FQ): the BUILT WPF app against a real loopback daemon. The item is posted through the real
/// <c>post_attention</c> tool with a <c>questions</c> array; the toggle buttons, the Reset link and the text box are driven through UI Automation; what the
/// daemon stored when the answers were sent is read from the attention ledger.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfFocusQuestionnaireE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-questionnaire-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly string _main;

	public WpfFocusQuestionnaireE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? $"question number {i}" : $"answer number {i}").ToArray());
		_main = _env.Session.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, TestEngines.Claude(new StubStreamingManager()), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree, new CatalogTestModule(["allowed.example"])]));

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

	private static JsonObject Questionnaire() => new()
	{
		["kind"] = "ask",
		["title"] = "Plan questions",
		["body"] = "Context for the questions.",
		["questions"] = new JsonArray(
			new JsonObject { ["type"] = "yes_no", ["question"] = "Use the cache?", ["recommended"] = "yes" },
			new JsonObject { ["type"] = "single", ["question"] = "Which store?", ["choices"] = new JsonArray("Redis", "Memory", "Disk"), ["recommended"] = "Memory" },
			new JsonObject { ["type"] = "multi", ["question"] = "Which clients?", ["choices"] = new JsonArray("WPF", "Web", "Phone"), ["recommended_choices"] = new JsonArray("WPF", "Phone") },
			new JsonObject { ["type"] = "text", ["question"] = "Name it", ["recommended"] = "cache-v2" }),
	};

	private async Task<Process> StartAndWaitForFocusAsync()
	{
		var result = await _env.PostAsync(Questionnaire());
		Assert.DoesNotContain("error", result.ToJsonString(), StringComparison.OrdinalIgnoreCase);

		var process = _app.Start();
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", $"Focus: 1 open item(s) for {_main}");
		Assert.True(ready, "The app did not open the main session with its Focus item. Log:\n" + _app.ReadLog());
		Assert.True(await _clicker.ClickAsync(process, "Focus", StepTimeout), "The Focus view switch was not found. Log:\n" + _app.ReadLog());
		return process;
	}

	private async Task<bool> WaitForAsync(Func<bool> condition)
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

	[Fact]
	public async Task TC_FQ_WPF_01_A_questionnaire_card_shows_the_recommendations_changes_reset_and_sends_the_answers_with_an_empty_note()
	{
		using var process = await StartAndWaitForFocusAsync();
		try
		{
			// Every question is there, as toggle buttons named Q<n> and the label; the text box is named after its question.
			foreach (var name in new[] { "Q1 Yes", "Q1 No", "Q2 A  Redis", "Q2 B  Memory", "Q2 C  Disk", "Q3 A  WPF", "Q3 B  Web", "Q3 C  Phone" })
				Assert.True(await _clicker.WaitForButtonAsync(process, name, StepTimeout), $"The button '{name}' was not found. Log:\n" + _app.ReadLog());
			Assert.Equal("cache-v2", _clicker.GetText(process, "Q4 answer"));
			Assert.False(_clicker.Exists(process, "Q1 Reset"), "A question at its recommendation shows no Reset link.");

			// Change Q1 (No), Q2 (A, the radio moves) and Q4 (own text): the Reset links appear.
			Assert.True(await _clicker.ToggleAsync(process, "Q1 No", StepTimeout));
			Assert.True(await _clicker.ToggleAsync(process, "Q2 A  Redis", StepTimeout));
			Assert.True(await _clicker.SetTextAsync(process, "Q4 answer", "mine", StepTimeout));
			Assert.True(await _clicker.WaitForButtonAsync(process, "Q1 Reset", StepTimeout), "The Reset link of Q1 did not show. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.WaitForButtonAsync(process, "Q2 Reset", StepTimeout));

			// Reset Q1 back to the recommendation, and send with no note typed: the answers are the reply.
			Assert.True(await _clicker.ClickAsync(process, "Q1 Reset", StepTimeout));
			Assert.True(await WaitForAsync(() => !_clicker.Exists(process, "Q1 Reset")), "Reset did not restore Q1.");
			Assert.True(await _clicker.ClickAsync(process, "Send answers", StepTimeout), "Send answers was not available for a questionnaire with an empty note. Log:\n" + _app.ReadLog());

			Assert.True(await WaitForAsync(() => _env.Attention.GetItems(_main).Single().Status == AttentionStatus.Answered), "The item was not answered. Log:\n" + _app.ReadLog());
			var answer = _env.Attention.GetItems(_main).Single().Answer!;
			Assert.Contains("Q1 Use the cache?: Yes (as recommended)", answer);
			Assert.Contains("Q2 Which store?: A Redis (changed; recommended: B Memory)", answer);
			Assert.Contains("Q3 Which clients?: A WPF, C Phone (as recommended)", answer);
			Assert.Contains("Q4 Name it: mine (changed)", answer);
			Assert.True(await _app.WaitForLogAsync(process, StepTimeout, $"Focus: 0 open item(s) for {_main}"), "The card did not leave the Focus view. Log:\n" + _app.ReadLog());
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
