using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.Tests.Services;

namespace TessynDesktop.Tests.Contract;

/// <summary>Index activity the test sets by hand.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeSessionIndexActivity : ISessionIndexActivity
{
	public int ActiveScans { get; set; }
	public DateTimeOffset? LastScanFinishedUtc { get; set; }
}

/// <summary>
/// Package J4 over a real loopback <see cref="TdcpServer"/>: the real tree, import service, scheduler, attention ledger and the five J4 modules,
/// with a canned session index and a hand-set clock. The tree holds the directory "proj" with the session "Wire session". Disposing deletes the temp directories.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class ImportWireEnv : IDisposable
{
	public static readonly DateTimeOffset Started = new(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);

	public ImportTestEnv Import { get; } = new();
	public AttentionTestEnv Attention { get; } = new();
	public FakeTurnService Turns { get; } = new();
	public SchedulerService Scheduler { get; }
	public ScheduleApiService Schedules { get; }
	public FakeSessionIndexActivity IndexActivity { get; } = new();
	public GlobalSearchService Search { get; }
	public DaemonHealthService Health { get; }
	public AttentionOverviewService Overview { get; }
	public RemoteTreeReader Reader { get; }
	public RemoteEventBus Bus { get; } = new();
	public TdcpDispatcher Dispatcher { get; }
	public TdcpTestHost Host { get; }
	public string LogDirectory { get; }

	public DirectoryNodeModel Directory { get; }
	public SessionNodeModel Session { get; }

	public string Root => Import.Root;

	public ImportWireEnv(bool withServices = true)
	{
		Directory = Import.AddDirectory("proj");
		System.IO.Directory.CreateDirectory(Directory.Path);
		Session = Import.Registry.AddSession(Directory, "Wire session");

		Scheduler = new SchedulerService(Import.Settings, Turns, Import.Registry);
		Schedules = new ScheduleApiService(Scheduler, Import.Registry, Attention.Clock);
		Reader    = new RemoteTreeReader(Import.Settings);
		Search    = new GlobalSearchService(Import.Index, Reader, IndexActivity);
		Overview  = new AttentionOverviewService(Reader, Attention.Service);

		LogDirectory = Path.Combine(Import.Root, "logs");
		System.IO.Directory.CreateDirectory(LogDirectory);
		File.WriteAllBytes(Path.Combine(LogDirectory, "tessyn20261104.log"), new byte[1234]);

		var runInfo = new DaemonRunInfo();
		var log     = new EventLog(Bus);
		Health      = new DaemonHealthService(runInfo, Reader, Import.Registry, log, IndexActivity, Attention.Clock, Started, LogDirectory);

		Dispatcher = new TdcpDispatcher(
		[
			withServices
				? new ClientRpcModule(runInfo: runInfo, eventLog: log, import: Import.Service, search: Search, schedules: Schedules, attentionOverview: Overview, health: Health)
				: new ClientRpcModule(runInfo: runInfo, eventLog: log),
			new ImportRpcModule(Import.Service),
			new SearchRpcModule(Search),
			new ScheduleRpcModule(Schedules),
			new DaemonHealthRpcModule(Health),
			// attention.reply and attention.dismiss are not part of J4; the module only needs the overview here.
			new AttentionRpcModule(Import.Registry, Attention.Service, null!, null!, Overview),
		]);
		Host = new TdcpTestHost(Dispatcher, bus: Bus, runInfo: runInfo);
	}

	/// <summary>Calls a method over HTTP and returns the whole JSON-RPC response.</summary>
	public Task<JsonNode> CallAsync(string method, JsonNode? @params = null) => Host.CallAsync(method, @params);

	/// <summary>The same call through the dispatcher as the relay would make it (only remote-callable methods exist there).</summary>
	public async Task<JsonNode> CallAsRelayAsync(string method, JsonNode? @params = null)
	{
		var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = @params?.DeepClone() ?? new JsonObject() };
		return (await Dispatcher.HandleTextAsync(request.ToJsonString(), TdcpTransport.Relay, CancellationToken.None))!;
	}

	/// <summary>Adds a Claude session of the directory "proj" to the canned index and the folder, and returns its id.</summary>
	public string AddClaudeSession(string id, string prompt = "Fix the build", string? slug = null, int messages = 2)
	{
		slug ??= Constants.ClaudeSessions.BuildProjectSlug(Directory.Path);
		var path = Import.WriteClaudeSession(slug, id, prompt, messages);
		var summary = Import.SummaryOf(path);
		summary.ProjectSlug = slug;
		Import.Index.SummariesResult = [.. Import.Index.SummariesResult, summary];
		return id;
	}

	public void Dispose()
	{
		Host.Dispose();
		Bus.Dispose();
		Attention.Dispose();
		Import.Dispose();
	}
}
