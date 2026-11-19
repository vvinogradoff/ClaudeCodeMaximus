using System.Diagnostics;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// End to end for FR.9.20 / UC.RT.08 (TC.RT.15): a Focus reply typed in the BUILT WPF app makes a session that was last active days ago recent. The daemon side is the real thing:
/// the real activity service over real draft files, the real draft, tree and recent modules and the real notification publisher on the host's event bus (so the row refresh
/// travels as a <c>session.changed</c>, with no polling). The Recent dropdown (24-hour window, ordered by activity) is read through UI Automation: the session (kept in the list by its open Ask item) moves above the session whose only activity is older.
/// The recency colour itself has no UI Automation property; it follows the same time (the view-model tests cover the tier of a row from its state).
/// </summary>
/// <remarks>Created by Claude</remarks>
[Collection("wpf-app-e2e")]
public sealed class WpfReplyDraftRecencyE2ETests : IDisposable
{
	private const string ReplyBoxName = "Your answer";

	private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-replyrecency-e2e-" + Guid.NewGuid().ToString("N"));
	private readonly FocusRefsEnv _env = new();
	private readonly SessionNodeModel _other;
	private readonly DraftService _drafts;
	private readonly SessionEpochTracker _epochs;
	private readonly RemoteNotificationPublisher _publisher;
	private readonly ClientTestHost _host;
	private readonly AppProcessHarness _app;
	private readonly UiAutomationClicker _clicker = new();
	private readonly string _main;

	public WpfReplyDraftRecencyE2ETests()
	{
		Directory.CreateDirectory(_root);
		// The transcripts end on 2026-11-14: days before the run, so neither session is recent through its entries.
		_env.WriteBodies(Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? $"question {i}" : $"answer {i}").ToArray());
		_other = _env.AddSession("Other session", Enumerable.Range(0, 6).Select(i => i % 2 == 0 ? $"other question {i}" : $"other answer {i}").ToArray());
		_main = _env.Session.NodeId;
		_drafts = new DraftService(Path.Combine(_root, "drafts"));

		// The other session is recent only through a composer draft written an hour ago.
		_drafts.SaveDraft(_other.FileName, "typed an hour ago");
		File.SetLastWriteTimeUtc(Path.Combine(_root, "drafts", _other.FileName + Constants.DraftFileExtension), DateTime.UtcNow.AddHours(-1));

		var activity = new SessionActivityService(_env.Base.Files, _drafts);
		var states = new SessionStateBuilder(_env.Base.Registry, new StubStreamingManager(), _env.Attention, activity);
		var reader = new RemoteTreeReader(_env.Base.Settings);
		var labels = new FakeDirectoryLabelService();
		var tree = new TreeRemoteRpcModule(reader, _env.Base.Registry, states, activity, new TreeVersionTracker(), labels, new InProcFakeIndexService(), new FakeSessionSearchService());
		var recent = new RecentRemoteRpcModule(reader, states, activity, _env.Attention, new InProcFakeScheduler(), labels, _env.Base.Registry, _env.Base.Settings);
		_host = new ClientTestHost((run, log) => new TdcpDispatcher(
			[new ClientRpcModule(runInfo: run, eventLog: log, drafts: _drafts), .. _env.Modules, new DraftRpcModule(_env.Base.Registry, _drafts, _env.Attention), tree, recent, new CatalogTestModule([])]));

		var daemon = FakeDaemonProxy.Create(out var fake);
		foreach (var node in new[] { _env.Session, _other })
		{
			fake.ByFileName[node.FileName] = node;
			fake.ByNodeId[node.NodeId] = node;
		}

		_epochs = new SessionEpochTracker(_env.Base.Files);
		_publisher = new RemoteNotificationPublisher(daemon, _env.Base.Files, _host.Bus, new TreeVersionTracker(), _epochs, states, drafts: _drafts);
		_publisher.Start();

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
		_publisher.Stop();
		_epochs.Dispose();
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
		var ready = await _app.WaitForLogAsync(process, StartTimeout, "Connected to daemon run", $"Session {_main} opened", $"Focus: 1 open item(s) for {_main}");
		Assert.True(ready, "The app did not open the session. Log:\n" + _app.ReadLog());
		return process;
	}

	[Fact]
	public async Task A_reply_typed_in_the_Focus_view_puts_an_old_session_at_the_top_of_the_Recent_dropdown()
	{
		var posted = await _env.PostAsync(new JsonObject { ["kind"] = "ask", ["title"] = "Proceed with the migration?" });
		Assert.DoesNotContain("error", posted.ToJsonString(), StringComparison.OrdinalIgnoreCase);
		using var process = await StartAsync();
		try
		{
			// Without the reply draft the session (kept in the dropdown by its open Ask item, FR.9.8) has activity days old and sits below the other one,
			// whose composer draft is an hour old; the order is checked once, after the reply is typed (the open popup cannot be closed through UI Automation).
			Assert.True(await _clicker.ClickAsync(process, "Focus", StepTimeout));
			Assert.True(await _clicker.SetTextAsync(process, ReplyBoxName, "Yes, but only on staging", StepTimeout), "The reply box was not found. Log:\n" + _app.ReadLog());

			// The list reloads on the session.changed of the reply draft (no polling); wait for the order to flip.
			Assert.True(await _clicker.ToggleAsync(process, "Recent dropdown", StepTimeout));
			var flipped = false;
			var deadline = DateTime.UtcNow + StepTimeout;
			while (DateTime.UtcNow < deadline && !flipped)
			{
				var typed = _clicker.GetBounds(process, "Open " + _env.Session.Name);
				var older = _clicker.GetBounds(process, "Open " + _other.Name);
				flipped = typed != null && older != null && typed.Value.Top < older.Value.Top;
				if (!flipped)
					await Task.Delay(250);
			}

			Assert.True(flipped, "The session with the reply draft is not above the session with the older composer draft. Log:\n" + _app.ReadLog());
			Assert.DoesNotContain("[ERR]", _app.ReadLog());
		}
		finally
		{
			_app.Stop(process);
		}
	}
}
