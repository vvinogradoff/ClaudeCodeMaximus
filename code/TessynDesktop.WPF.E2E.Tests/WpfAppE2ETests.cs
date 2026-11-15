using System.Diagnostics;
using System.Text;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;
using static TessynDesktop.Tests.Services.RemoteTestHelpers;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end: the BUILT WPF exe against a real loopback daemon (the real <see cref="TdcpServer"/> with the real tree and session
/// modules over a seeded temp registry and session files). The app runs with <c>--data-root</c> and <c>--daemon-file</c> in a temp
/// directory, so it never touches the real %AppData%. The assertions read the app's own log: it connected, loaded the tree, restored
/// the last selection and opened that session, the page reported its first paint, no error was logged, and closing the window ends the
/// process. The WPF app itself never references Core; only this test does.
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfAppE2ETests : IDisposable
{
	private const int PagedEntryCount = 150;

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly RemoteSessionEnv _env = new();
	private readonly ClientTestHost _host;
	private readonly string _daemonFile;
	private readonly string _mainSessionId;
	private readonly string _directoryId;

	public WpfAppE2ETests()
	{
		Directory.CreateDirectory(_root);

		// Seed: one directory with the main session (150 entries, so tail-first paging has an older page) and two more sessions.
		var entries = new List<TessynDesktop.Models.SessionEntryModel>();
		for (var i = 0; i < PagedEntryCount; i++)
			entries.Add(i % 2 == 0 ? User($"question {i} with `code` and a [link](https://example.com/{i})", i) : Assistant($"**answer {i}**\n\n- one\n- two", i, "claude-opus-5-5", "high"));
		_env.Files.Set(_env.Session.FileName, entries);
		_env.Registry.AddSession(_env.Directory, "Second session");
		_env.Registry.AddSession(_env.Directory, "Third session");
		_mainSessionId = _env.Session.NodeId;
		_directoryId = _env.Directory.NodeId;

		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(_env.Registry, _env.Streaming, _env.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(_env.Settings), _env.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		_host = new ClientTestHost((run, log) => new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log), _env.Module, tree]));

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		_daemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(_host.RunInfo, tokens, _daemonFile).Write(_host.Port);

		// Client state as a previous run would have left it: the directory expanded and the main session selected.
		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{_mainSessionId}\",\"expandedNodeIds\":[\"{_directoryId}\"]}}");
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

	[Fact]
	public async Task The_built_app_connects_loads_the_tree_opens_a_session_logs_no_error_and_exits_when_closed()
	{
		using var process = StartApp();
		try
		{
			var log = new StringBuilder();
			var ready = await WaitForLogAsync(process, log, TimeSpan.FromSeconds(60),
				"Connected to daemon run", "Tree loaded: 3 sessions", $"Session {_mainSessionId} opened", "session-switch");

			Assert.True(ready, "The app did not reach connect, tree load, session open and first paint in time. Log:\n" + log);
			Assert.Contains($"Session {_mainSessionId} opened: 100 of {PagedEntryCount} entries loaded (cold)", log.ToString());
			Assert.DoesNotContain("[ERR]", log.ToString());

			Assert.True(new AppWindowFinder().Close(process), "The main window could not be closed.");
			Assert.True(process.WaitForExit(TimeSpan.FromSeconds(20)), "The app is still running 20 s after its window was closed.");

			var finalLog = ReadLog();
			Assert.DoesNotContain("[ERR]", finalLog);
			Assert.DoesNotContain("[FTL]", finalLog);
			Assert.Contains(_mainSessionId, await File.ReadAllTextAsync(Path.Combine(_root, "clients", "wpf.json")));
		}
		finally
		{
			if (!process.HasExited)
				process.Kill(entireProcessTree: true);
		}
	}

	private Process StartApp()
	{
		var exe = new BuiltAppLocator().FindExe();
		var info = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
		info.ArgumentList.Add("--test-window");
		info.ArgumentList.Add("--data-root");
		info.ArgumentList.Add(_root);
		info.ArgumentList.Add("--daemon-file");
		info.ArgumentList.Add(_daemonFile);
		return Process.Start(info)!;
	}

	private async Task<bool> WaitForLogAsync(Process process, StringBuilder into, TimeSpan timeout, params string[] needles)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var text = ReadLog();
			into.Clear().Append(text);
			if (needles.All(n => text.Contains(n, StringComparison.Ordinal)))
				return true;
			await Task.Delay(250);
		}

		into.Clear().Append(ReadLog());
		return false;
	}

	private string ReadLog()
	{
		var dir = Path.Combine(_root, "logs");
		if (!Directory.Exists(dir))
			return string.Empty;

		var text = new StringBuilder();
		foreach (var file in Directory.EnumerateFiles(dir, "wpf-*.txt").OrderBy(f => f, StringComparer.Ordinal))
		{
			using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			using var reader = new StreamReader(stream);
			text.Append(reader.ReadToEnd());
		}

		return text.ToString();
	}
}
