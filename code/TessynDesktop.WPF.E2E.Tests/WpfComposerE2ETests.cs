using System.Diagnostics;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for file drag-and-drop in the prompt input (FR.31.2): the BUILT WPF app against a real loopback daemon (real
/// <see cref="TdcpServer"/>, real <see cref="AttachmentRpcModule"/> and <see cref="AttachmentService"/> over a temp data root). The OLE drag
/// itself cannot be driven without moving the user's mouse, so the files are handed over through the <c>--test-drop</c> seam (only honoured
/// with <c>--test-window</c>), which calls the same view-model entry point as the drop handler; everything behind the drop target is real:
/// the file-mode toggle restored from client state, the <c>attachment.import</c> call over TDCP, the copy on disk, the inserted text. The
/// toggle is pressed through UI Automation, and the prompt input is read through UI Automation. The drop target itself (<c>FileDropReader</c>,
/// the XAML handlers, paste) is covered by <c>ComposerViewTests</c> with real routed events.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfComposerE2ETests : IDisposable
{
	private const string InputName = "Prompt input";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-composer-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly string _main;
	private readonly string _sources;

	public WpfComposerE2ETests()
	{
		Directory.CreateDirectory(_root);
		_env.WriteBodies(Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_main = _env.Session.NodeId;
		_sources = Path.Combine(_env.Base.Root, "dropped");
		Directory.CreateDirectory(_sources);

		var attachments = new AttachmentService(_env.Base.Registry, _env.Base.Settings, _ => null, null, _root, _env.Clock, userProfile: _env.UserProfile);
		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Base.Registry, TestEngines.Claude(new StubStreamingManager()), _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Base.Settings), _env.Base.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), .. _env.Modules, tree, new CatalogTestModule([]), new AttachmentRpcModule(attachments)]));

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

	/// <summary>Client state as a previous run left it: the directory expanded, the main session selected and the file mode as given.</summary>
	private void SeedClientState(bool fileMode)
	{
		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_main}\",\"expandedNodeIds\":[\"{_env.Base.Directory.NodeId}\"],\"attachAsFiles\":{(fileMode ? "true" : "false")}}}");
	}

	private string Source(string name, string content)
	{
		var path = Path.Combine(_sources, name);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, content);
		return path;
	}

	private string AttachmentsFolder => Path.Combine(_root, "attachments", "default", _main);

	private async Task<Process> StartAsync(string handledLine, params string[] drops)
	{
		var arguments = drops.SelectMany(d => new[] { "--test-drop", d }).ToArray();
		var process = _app.Start(arguments);
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", handledLine);
		Assert.True(ready, $"The drop was not handled ('{handledLine}'). Log:\n" + _app.ReadLog());
		return process;
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

	private void AssertNoErrors()
	{
		var log = _app.ReadLog();
		Assert.DoesNotContain("[ERR]", log);
		Assert.DoesNotContain("[FTL]", log);
	}

	[Fact]
	public async Task In_file_mode_dropped_files_are_copied_into_the_session_attachments_and_their_paths_are_inserted()
	{
		SeedClientState(fileMode: true);
		var report = Source("quarterly report.md", "# Q3\n");
		var photo = Source("photo.png", "not really a png");
		using var process = await StartAsync($"Composer: 2 file(s) handled in file mode for {_main}", report, photo);
		try
		{
			var text = await WaitForInputAsync(process, t => t.Contains("photo.png", StringComparison.Ordinal));

			// On disk: under attachments\<profile>\<node>\ of the temp data root, timestamp-prefixed, the content copied, the sources untouched.
			var stored = Directory.GetFiles(AttachmentsFolder).OrderBy(f => f, StringComparer.Ordinal).ToArray();
			Assert.Equal(2, stored.Length);
			var storedPhoto = Assert.Single(stored, f => f.EndsWith("-photo.png", StringComparison.Ordinal));
			var storedReport = Assert.Single(stored, f => f.EndsWith("-quarterly report.md", StringComparison.Ordinal));
			Assert.Equal("# Q3\n", File.ReadAllText(storedReport));
			Assert.Equal("not really a png", File.ReadAllText(storedPhoto));
			Assert.True(File.Exists(report) && File.Exists(photo));

			// In the input: one "Attached file: <returned path>" line per file, in drop order.
			var lines = text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
			Assert.Equal(["Attached file: " + storedReport, "Attached file: " + storedPhoto], lines);
			Assert.False(_clicker.TextExists(process, "Could not attach"));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task In_file_mode_a_refused_file_and_a_folder_attach_nothing_and_show_a_notice_not_a_dialog()
	{
		SeedClientState(fileMode: true);
		var secret = Source("credentials.json", "{}");
		var folder = Path.Combine(_sources, "a folder");
		Directory.CreateDirectory(folder);
		var good = Source("ok.txt", "fine");
		using var process = await StartAsync($"Composer: 3 file(s) handled in file mode for {_main}", secret, folder, good);
		try
		{
			var text = await WaitForInputAsync(process, t => t.Contains("ok.txt", StringComparison.Ordinal));

			Assert.EndsWith("-ok.txt", Assert.Single(Directory.GetFiles(AttachmentsFolder)));
			Assert.DoesNotContain("credentials", text);
			Assert.DoesNotContain("a folder", text);
			Assert.Single(text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries));
			Assert.True(_clicker.TextExists(process, "credentials.json"), "The refusal of the denied file was not shown. Log:\n" + _app.ReadLog());
			Assert.True(_clicker.TextExists(process, "a folder"), "The refusal of the folder was not shown.");
			Assert.True(_clicker.Exists(process, "Dismiss message"));   // the notice is dismissable, the window is still usable
			Assert.False(process.HasExited);
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task In_path_mode_dropped_files_and_folders_insert_their_own_paths_and_copy_nothing()
	{
		SeedClientState(fileMode: false);
		var file = Source("notes.txt", "n");
		var folder = Path.Combine(_sources, "some folder");
		Directory.CreateDirectory(folder);
		using var process = await StartAsync("Composer: 2 file(s) handled in path mode", file, folder);
		try
		{
			var text = await WaitForInputAsync(process, t => t.Contains("some folder", StringComparison.Ordinal));

			Assert.Equal([file, folder], text.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries));
			Assert.False(Directory.Exists(Path.Combine(_root, "attachments")));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(process);
		}
	}

	[Fact]
	public async Task The_file_mode_toggle_is_remembered_across_runs()
	{
		SeedClientState(fileMode: false);
		var state = Path.Combine(_root, "clients", "wpf.json");
		var process = _app.Start();
		try
		{
			Assert.True(await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened"), "The session did not open. Log:\n" + _app.ReadLog());
			Assert.True(await _clicker.ToggleAsync(process, "File mode", StepTimeout), "The file mode toggle was not found. Log:\n" + _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
			process.Dispose();
		}

		Assert.Contains("\"attachAsFiles\": true", File.ReadAllText(state));

		// A second run starts in file mode: a dropped file is copied.
		var file = Source("again.txt", "a");
		using var second = await StartAsync($"Composer: 1 file(s) handled in file mode for {_main}", file);
		try
		{
			await WaitForInputAsync(second, t => t.Contains("again.txt", StringComparison.Ordinal));
			Assert.Single(Directory.GetFiles(AttachmentsFolder));
			AssertNoErrors();
		}
		finally
		{
			_app.Stop(second);
		}
	}
}
