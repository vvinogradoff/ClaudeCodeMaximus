using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.Tests.Client;
using TessynDesktop.Tests.Services;
using TessynDesktop.Tests.Services.ElevatedRuns;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// The daemon side of the elevated-run E2E tests (TC.ER.56 to TC.ER.61): the real session, attention and file modules over a temp registry (<see cref="FocusRefsEnv"/>), the REAL
/// <see cref="ElevatedRunService"/> over a temp run folder with the real wrapper script, the real <c>elevated.*</c> and <c>attention.*</c> RPC modules behind a loopback
/// <see cref="ClientTestHost"/>, and the real MCP server, so a request is made with <c>request_elevated_run</c> exactly as an agent does. The outcome turn goes to a recording
/// scheduler (the delivery the daemon makes to the requesting session); nothing here ever elevates: the app runs with <c>--elevated-launcher=fake:&lt;mode&gt;</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ElevatedRunE2EEnv : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "tessyn-wpf-elevated-e2e-" + Guid.NewGuid().ToString("N"));

	public ElevatedRunE2EEnv()
	{
		Directory.CreateDirectory(_root);
		Refs.WriteBodies("Hello from the lead session", "Hello back");
		Wrk = Refs.AddSession("Wrk", "Hello from the worker", "Hello back");
		lock (Refs.Base.Settings.SyncRoot)
		{
			Wrk.SupervisorNodeId = Lead.NodeId;
			Wrk.OrchestrationDepth = 1;
		}

		RunsRoot = Path.Combine(_root, "elevated-runs");
		Service = new ElevatedRunService(Refs.Base.Settings, Refs.Base.Registry, Refs.Attention, new Lazy<ISchedulerService>(() => Scheduler), Channels, RunsRoot, isElevated: false);
		Mcp = new AgentMcpServer(
			Refs.Base.Settings,
			new Lazy<ISessionTurnService>(() => throw new NotSupportedException()),
			new Lazy<ISchedulerService>(() => Scheduler),
			Refs.Base.Registry, new FakeSessionFileService(),
			new ScriptedModelService(), tdcpServer: null!, configMerger: null!,
			new FakeTelegramBridge(), new FakeNotificationService(), new FakeDirectoryLabelService(),
			Refs.Attention, TagTestEnv.Unused(),
			elevatedRuns: Service);

		Host = new ClientTestHost(BuildDispatcher);
		Service.Changed += (_, e) => Host.Bus.Publish(RemoteProtocol.ElevatedRuns.ChangedEvent, new ElevatedChangedPayload(e.RequestId, e.NodeId, e.Status));
		Refs.Attention.AttentionChanged += (_, e) => Host.Bus.Publish(RemoteProtocol.Events.AttentionChanged, new AttentionChangedPayload(e.NodeId, Refs.Attention.GetSummary(e.NodeId).ToDto()));
		Service.Start();

		var tokens = new TdcpTokenStore(Path.Combine(_root, "tdcp-token.dat"));
		tokens.Save(ClientTestHost.Token);
		DaemonFile = Path.Combine(_root, "daemon.json");
		new DaemonDiscoveryFile(Host.RunInfo, tokens, DaemonFile).Write(Host.Port);

		// Client state as a previous run left it: the directory expanded and the lead session selected.
		var clients = Directory.CreateDirectory(Path.Combine(_root, "clients"));
		File.WriteAllText(Path.Combine(clients.FullName, "wpf.json"),
			$"{{\"lastSelectedNodeId\":\"{Lead.NodeId}\",\"expandedNodeIds\":[\"{Refs.Base.Directory.NodeId}\"]}}");
		App = new AppProcessHarness(_root, DaemonFile);
	}

	public string Root => _root;
	public string DaemonFile { get; }
	public FocusRefsEnv Refs { get; } = new();
	public RecordingScheduler Scheduler { get; } = new();
	public FakeUserChannels Channels { get; } = new();
	public ElevatedRunService Service { get; }
	public AgentMcpServer Mcp { get; }
	public ClientTestHost Host { get; }
	public AppProcessHarness App { get; }
	public string RunsRoot { get; }
	public SessionNodeModel Lead => Refs.Session;
	public SessionNodeModel Wrk { get; }
	public string WorkDir => Refs.WorkDir;

	private TdcpDispatcher BuildDispatcher(IDaemonRunInfo run, IEventLog log)
	{
		var b = Refs.Base;
		var activity = new ActivityByNodeService();
		var states = new SessionStateBuilder(b.Registry, new StubStreamingManager(), Refs.Attention, activity);
		var tree = new TreeRemoteRpcModule(new RemoteTreeReader(b.Settings), b.Registry, states, activity, new TreeVersionTracker(),
			new FakeDirectoryLabelService(), new InProcFakeIndexService(), new FakeSessionSearchService());
		var labels = new ProfileLabelService(b.ProfileService, b.Settings);
		var attention = new AttentionRpcModule(b.Registry, Refs.Attention, new AttentionReplyService(Refs.Attention, new RecordingSendService()),
			new TurnOptionsResolver(b.Models, b.Settings, labels), new SessionModelSettings(b.Settings, b.Models), elevatedRuns: Service);
		var modules = Refs.Modules.Where(m => m is not AttentionRpcModule).Append(attention).Append(new ElevatedRunRpcModule(Service));
		return new TdcpDispatcher([new ClientRpcModule(runInfo: run, eventLog: log, elevatedRuns: Service), .. modules, tree, new CatalogTestModule([])]);
	}

	/// <summary>The command of an <c>request_elevated_run</c> call made as <paramref name="caller"/> (default: the lead session); returns the new request's id.</summary>
	public async Task<string> RequestAsync(string command, string reason = "Run the suite as administrator", SessionNodeModel? caller = null, int? timeoutMinutes = null)
	{
		var args = new JsonObject { ["command"] = command, ["workingDir"] = WorkDir, ["reason"] = reason };
		if (timeoutMinutes is { } minutes)
			args["timeoutMinutes"] = minutes;

		var before = Service.List(null, all: true, max: 100).Select(r => r.Id).ToHashSet();
		var result = await Mcp.HandleToolCallAsync(new JsonObject { ["name"] = "request_elevated_run", ["arguments"] = args }, caller ?? Lead);
		Assert.DoesNotContain("error", result.ToJsonString(), StringComparison.OrdinalIgnoreCase);
		return Service.List(null, all: true, max: 100).Single(r => !before.Contains(r.Id)).Id;
	}

	/// <summary>Waits until the run has this status (polling the real service); null on timeout.</summary>
	public async Task<ElevatedRunModel?> WaitForStatusAsync(string id, TimeSpan timeout, params ElevatedRunStatus[] statuses)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			var run = Service.Find(id);
			if (run != null && statuses.Contains(run.Status))
				return run;

			await Task.Delay(200);
		}

		return Service.Find(id) is { } last && statuses.Contains(last.Status) ? last : null;
	}

	/// <summary>The outcome turn delivered to <paramref name="nodeId"/> for the run, or null.</summary>
	public string? OutcomeFor(string nodeId, string runId)
	{
		lock (Scheduler.ElevatedDeliveries)
			return Scheduler.ElevatedDeliveries.FirstOrDefault(d => d.target == nodeId && d.runId == runId).text;
	}

	/// <summary>Every delivery of the run, to whichever session.</summary>
	public IReadOnlyList<(string target, string runId, string text)> DeliveriesOf(string runId)
	{
		lock (Scheduler.ElevatedDeliveries)
			return Scheduler.ElevatedDeliveries.Where(d => d.runId == runId).ToList();
	}

	public void Dispose()
	{
		Service.Stop();
		Host.Dispose();
		Refs.Dispose();
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
			// WebView2 helper processes can hold the data folder for a moment; the folder is in %TEMP%.
		}
		catch (UnauthorizedAccessException)
		{
			// A wrapper process may still hold a run folder: temp data only.
		}
	}
}

/// <summary>Reads the text elements of the running app through UI Automation (the card's lines are plain text elements named by their text).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class UiAutomationNames
{
	/// <summary>The names of every Text element of the app window; empty while the window is not there.</summary>
	public IReadOnlyList<string> TextNames(Process process)
	{
		var hwnd = new AppWindowFinder().Find(process.Id);
		if (hwnd == IntPtr.Zero)
			return [];

		try
		{
			return AutomationElement.FromHandle(hwnd).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
				.Cast<AutomationElement>().Select(e => e.Current.Name).ToList();
		}
		catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
		{
			return [];
		}
	}

	/// <summary>Waits until a Text element's name satisfies <paramref name="match"/>; the matching name, or null on timeout.</summary>
	public async Task<string?> WaitForTextAsync(Process process, Func<string, bool> match, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline && !process.HasExited)
		{
			var found = TextNames(process).FirstOrDefault(match);
			if (found != null)
				return found;

			await Task.Delay(250);
		}

		return null;
	}
}
