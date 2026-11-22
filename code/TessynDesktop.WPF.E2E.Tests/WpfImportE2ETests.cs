using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for the import picker and the global search (W7, FR.13): the BUILT WPF app against a real loopback daemon with the real <c>import.*</c> methods (real
/// <see cref="ImportService"/> over Claude session files written under a temp projects folder) and a scripted <c>search.global</c>. The title-bar Import button, the picker's
/// ticks, the Global toggle, the search box and Import selected are driven through UI Automation; the result is read from the daemon's tree and from the tree rows of the app.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfImportE2ETests : IDisposable
{
	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-import-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly string _projects;
	private readonly FocusRefsEnv _env = new();
	private readonly ScriptedSearch _search = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly UiAutomationTree _rows = new();
	private readonly string _main;

	public WpfImportE2ETests()
	{
		Directory.CreateDirectory(_root);
		_projects = Directory.CreateDirectory(Path.Combine(_root, "claude-projects")).FullName;
		_env.WriteBodies(Enumerable.Range(0, 4).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_main = _env.Session.NodeId;

		var import = new ImportService(new ClaudeSessionImportService([_projects]), new InProcFakeIndexService(), _env.Base.Files, _env.Base.Registry, _env.Base.Settings, [_projects]);
		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, TestEngines.Claude(new StubStreamingManager()), _env.Attention, activity);
		var versions = new TreeVersionTracker();
		// What the daemon's notification publisher does: a structural change of the tree is a new tree version.
		_env.Base.Registry.TreeChanged += (_, e) =>
		{
			if (e.Kind != TreeChangeKind.DaemonMutation && e.Kind != TreeChangeKind.SessionIsRunningChanged)
				versions.Increment();
		};
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, versions,
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher(
			[new ClientRpcModule(runInfo: run, eventLog: log, import: import, search: _search), .. _env.Modules, tree, new ImportRpcModule(import), new SearchRpcModule(_search), new CatalogTestModule([])]));

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

	/// <summary>Writes a Claude Code session (user, assistant) under the project folder of <paramref name="workingDirectory"/>.</summary>
	private void WriteClaudeSession(string workingDirectory, string sessionId, string firstPrompt)
	{
		var folder = Directory.CreateDirectory(Path.Combine(_projects, Constants.ClaudeSessions.BuildProjectSlug(workingDirectory))).FullName;
		var lines = new[]
		{
			$$"""{"type":"user","message":{"role":"user","content":{{JsonSerializer.Serialize(firstPrompt)}}},"timestamp":"2026-03-10T10:00:00Z","uuid":"u0","sessionId":"{{sessionId}}","cwd":{{JsonSerializer.Serialize(workingDirectory)}}}""",
			$$"""{"type":"assistant","message":{"content":[{"type":"text","text":"an answer"}]},"timestamp":"2026-03-10T10:00:01Z","uuid":"a1","sessionId":"{{sessionId}}","cwd":{{JsonSerializer.Serialize(workingDirectory)}}}""",
		};
		File.WriteAllLines(Path.Combine(folder, sessionId + ".jsonl"), lines, new UTF8Encoding(false));
	}

	/// <summary>The sessions of the test directory (imports land at its top level).</summary>
	private List<SessionNodeModel> ImportedSessions()
	{
		lock (_env.Base.Settings.SyncRoot)
			return [.. _env.Base.Directory.Sessions];
	}

	private async Task<Process> StartAsync()
	{
		var process = _app.Start();
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", "click-to-first-paint");
		Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());
		return process;
	}

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
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

	private async Task OpenPickerAsync(Process process)
	{
		Assert.True(await _clicker.ClickMainMenuItemAsync(process, Constants.ImportMenuItem, StepTimeout), "The Import entry of the main menu was not found. Log:\n" + _app.ReadLog());
		Assert.True(await WaitForAsync(() => FindControl(process, ControlType.Button, "Import selected") != null), "The picker did not open. Log:\n" + _app.ReadLog());
	}

	private static AutomationElement? FindControl(Process process, ControlType type, string name)
	{
		var hwnd = new AppWindowFinder().Find(process.Id);
		if (hwnd == IntPtr.Zero)
			return null;

		try
		{
			return AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, new AndCondition(
				new PropertyCondition(AutomationElement.ControlTypeProperty, type), new PropertyCondition(AutomationElement.NameProperty, name)));
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
		{
			return null;
		}
	}

	/// <summary>Ticks (or unticks) a check box of the picker through its TogglePattern.</summary>
	private async Task<bool> ToggleCheckBoxAsync(Process process, string name)
	{
		var deadline = DateTime.UtcNow + StepTimeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			try
			{
				var box = FindControl(process, ControlType.CheckBox, name);
				if (box != null && box.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern))
				{
					((TogglePattern)pattern).Toggle();
					return true;
				}
			}
			catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException)
			{
				// the row is being rebuilt; try again
			}

			await Task.Delay(250);
		}

		return false;
	}

	private static bool IsEnabled(Process process, string name) => FindControl(process, ControlType.CheckBox, name)?.Current.IsEnabled == true;

	[Fact]
	public async Task A_session_picked_in_the_picker_is_imported_into_the_directory_and_appears_in_the_tree()
	{
		WriteClaudeSession(_env.WorkDir, "11111111-aaaa-bbbb-cccc-000000000001", "Import this fixture question");
		WriteClaudeSession(_env.WorkDir, "11111111-aaaa-bbbb-cccc-000000000002", "Leave this one alone");
		using var process = await StartAsync();
		try
		{
			await OpenPickerAsync(process);
			Assert.True(await WaitForAsync(() => FindControl(process, ControlType.CheckBox, "Select 11111111-aaaa-bbbb-cccc-000000000001") != null), "The picker listed no session. Log:\n" + _app.ReadLog());
			Assert.True(await ToggleCheckBoxAsync(process, "Select 11111111-aaaa-bbbb-cccc-000000000001"));

			Assert.True(await _clicker.ClickAsync(process, "Import selected", StepTimeout));

			Assert.True(await WaitForAsync(() => ImportedSessions().Any(s => s.ExternalId == "11111111-aaaa-bbbb-cccc-000000000001")), "The daemon did not import the session. Log:\n" + _app.ReadLog());
			Assert.DoesNotContain(ImportedSessions(), s => s.ExternalId == "11111111-aaaa-bbbb-cccc-000000000002");
			Assert.True(await _rows.WaitForRowAsync(process, "Import this fixture question", shouldExist: true, StepTimeout), "The imported session is not in the tree. Log:\n" + _app.ReadLog());
			Assert.True(await WaitForAsync(() => FindControl(process, ControlType.Button, "Import selected") == null), "The picker did not close after a complete import.");
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task An_already_imported_session_is_greyed_and_cannot_be_ticked()
	{
		WriteClaudeSession(_env.WorkDir, "22222222-aaaa-bbbb-cccc-000000000001", "Fresh one");
		WriteClaudeSession(_env.WorkDir, "22222222-aaaa-bbbb-cccc-000000000002", "Already here");
		var existing = _env.Base.Registry.AddSession(_env.Base.Directory, "Already here");
		existing.ExternalId = "22222222-aaaa-bbbb-cccc-000000000002";
		using var process = await StartAsync();
		try
		{
			await OpenPickerAsync(process);
			Assert.True(await WaitForAsync(() => FindControl(process, ControlType.CheckBox, "Select 22222222-aaaa-bbbb-cccc-000000000001") != null), "The picker listed no session. Log:\n" + _app.ReadLog());

			Assert.True(IsEnabled(process, "Select 22222222-aaaa-bbbb-cccc-000000000001"));
			Assert.False(IsEnabled(process, "Select 22222222-aaaa-bbbb-cccc-000000000002"));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task Global_search_finds_a_session_of_another_project_and_imports_it()
	{
		const string other = "33333333-aaaa-bbbb-cccc-000000000009";
		var otherFolder = Path.Combine(_root, "elsewhere", "other-project");
		Directory.CreateDirectory(otherFolder);
		WriteClaudeSession(otherFolder, other, "Quarterly report question");
		_search.Hits = [new SearchHitDto(other, null, Constants.ClaudeSessions.BuildProjectSlug(otherFolder), "…the quarterly report numbers…", 4.2, null)];
		using var process = await StartAsync();
		try
		{
			await OpenPickerAsync(process);
			Assert.True(await ToggleCheckBoxAsync(process, "Global search"));
			Assert.True(await _clicker.SetTextAsync(process, "Import search", "quarterly", StepTimeout), "The search box was not found. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ClickAsync(process, "Search sessions", StepTimeout));

			Assert.True(await WaitForAsync(() => FindControl(process, ControlType.CheckBox, "Select " + other) != null), "The global hit was not listed. Log:\n" + _app.ReadLog());
			Assert.Equal("quarterly", _search.LastQuery);
			Assert.True(await ToggleCheckBoxAsync(process, "Select " + other));
			Assert.True(await _clicker.ClickAsync(process, "Import selected", StepTimeout));

			Assert.True(await WaitForAsync(() => ImportedSessions().Any(s => s.ExternalId == other)), "The daemon did not import the session. Log:\n" + _app.ReadLog());
			Assert.True(await _rows.WaitForRowAsync(process, "Quarterly report question", shouldExist: true, StepTimeout), "The imported session is not in the tree. Log:\n" + _app.ReadLog());
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	private static class Constants
	{
		public const string ImportMenuItem = "Import sessions…";

		public static class ClaudeSessions
		{
			public static string BuildProjectSlug(string workingDirectory) => TessynDesktop.Constants.ClaudeSessions.BuildProjectSlug(workingDirectory);
		}
	}

	/// <summary>A <c>search.global</c> that answers a given list of hits (the real one needs a filled session index).</summary>
	private sealed class ScriptedSearch : IGlobalSearchService
	{
		public IReadOnlyList<SearchHitDto> Hits { get; set; } = [];

		public string? LastQuery { get; private set; }

		public Task<SearchGlobalResult> SearchAsync(string query, string? projectSlug, int? limit, int? offset, CancellationToken cancellationToken = default)
		{
			LastQuery = query;
			return Task.FromResult(new SearchGlobalResult(Hits, Hits.Count));
		}

		public SearchReindexResult StartReindex() => new(false);
	}
}
