using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services;

/// <summary>Collects the Serilog events a service writes, so a test can read the audit log.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class CollectingSink : ILogEventSink
{
	public List<LogEvent> Events { get; } = [];

	public void Emit(LogEvent logEvent)
	{
		lock (Events)
			Events.Add(logEvent);
	}

	public string[] Messages()
	{
		lock (Events)
			return [.. Events.ConvertAll(e => e.Level + ": " + e.RenderMessage())];
	}
}

/// <summary>
/// Focus references (step 1): real session files and content store (from <see cref="EntryIdsEnv"/>), a real attention service and
/// ledger store in a temp folder, the real resolver and file-access service, the MCP server and one dispatcher with the session,
/// attention and file modules - the daemon's wiring without the HTTP listeners.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FocusRefsEnv : IDisposable
{
	public EntryIdsEnv Base { get; } = new();
	public string LedgerRoot { get; }
	public string DataRoot { get; }
	public string WorkDir => Base.Directory.Path;
	public CollectingSink Log { get; } = new();
	public AttentionLedgerStore LedgerStore { get; }
	public ManualTimeProvider Clock { get; } = new();
	public AttentionService Attention { get; private set; }
	public FocusRefResolver Resolver { get; }
	public FileAccessService Files { get; private set; }
	public TdcpDispatcher Dispatcher { get; private set; } = null!;

	/// <summary>The modules of <see cref="Dispatcher"/>, for a host that adds <c>client.hello</c> around them.</summary>
	public IRemoteRpcModule[] Modules { get; private set; } = [];
	public AgentMcpServer Mcp { get; }

	public SessionNodeModel Session => Base.Session;

	public FocusRefsEnv()
	{
		LedgerRoot = Path.Combine(Base.Root, "attention");
		DataRoot   = Path.Combine(Base.Root, "data-root");
		Directory.CreateDirectory(WorkDir);
		Directory.CreateDirectory(DataRoot);

		LedgerStore = new AttentionLedgerStore(LedgerRoot);
		Attention   = new AttentionService(LedgerStore, Base.Files, Clock);
		Resolver    = new FocusRefResolver(Base.Registry, Base.Store, DataRoot);
		Files       = NewFiles(Attention);

		Mcp = new AgentMcpServer(
			Base.Settings,
			new Lazy<ISessionTurnService>(() => new FakeTurnService()),
			new Lazy<ISchedulerService>(() => new RecordingScheduler()),
			Base.Registry,
			fileService: null!,
			modelService: null!,
			tdcpServer: null!,
			configMerger: null!,
			new FakeTelegramBridge(),
			new FakeNotificationService(),
			new FakeDirectoryLabelService(),
			Attention,
			TagTestEnv.Unused(),
			diagnostics: null,
			refResolver: Resolver);

		BuildDispatcher();
	}

	private FileAccessService NewFiles(IAttentionService attention) =>
		new(Base.Registry, attention, Base.Reader, Base.Settings, DataRoot, Clock, log: new LoggerConfiguration().WriteTo.Sink(Log).CreateLogger());

	private void BuildDispatcher()
	{
		var labels = new ProfileLabelService(Base.ProfileService, Base.Settings);
		var options = new TurnOptionsResolver(Base.Models, Base.Settings, labels);
		var attentionModule = new AttentionRpcModule(Base.Registry, Attention, new AttentionReplyService(Attention, new RecordingSendService()), options);
		Modules    = [Base.Module, attentionModule, new FileRpcModule(Files)];
		Dispatcher = new TdcpDispatcher(Modules);
	}

	/// <summary>A restarted daemon: new attention service and file-access service over the same ledger files.</summary>
	public void Restart()
	{
		Attention = new AttentionService(new AttentionLedgerStore(LedgerRoot), Base.Files, Clock);
		Files     = NewFiles(Attention);
		BuildDispatcher();
	}

	// ── transcript ───────────────────────────────────────────────────────────

	/// <summary>Writes entries alternating user/assistant with the given bodies; entry i is ordinal i.</summary>
	public void WriteBodies(params string[] bodies) => WriteBodies(Session, bodies);

	public void WriteBodies(SessionNodeModel node, params string[] bodies)
	{
		var sb = new StringBuilder();
		for (var i = 0; i < bodies.Length; i++)
			sb.Append(i % 2 == 0
				? EntryIdsEnv.EntryText("USER", i, bodies[i], "profile=\"work\"")
				: EntryIdsEnv.EntryText("ASSISTANT", i, bodies[i], "model=\"opus\" effort=\"high\""));

		File.WriteAllText(Base.Files.GetFullPath(node.FileName), sb.ToString(), new UTF8Encoding(true));
	}

	public SessionNodeModel AddSession(string name, params string[] bodies)
	{
		var node = Base.Registry.AddSession(Base.Directory, name);
		WriteBodies(node, bodies);
		return node;
	}

	public string WriteFile(string relative, string text) => WriteFile(relative, Encoding.UTF8.GetBytes(text));

	public string WriteFile(string relative, byte[] bytes)
	{
		var full = Path.GetFullPath(Path.Combine(WorkDir, relative));
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		File.WriteAllBytes(full, bytes);
		return full;
	}

	// ── calls ────────────────────────────────────────────────────────────────

	public async Task<JsonNode> CallAsync(string method, string paramsJson, TdcpTransport transport)
	{
		var request = $$"""{"jsonrpc":"2.0","id":1,"method":"{{method}}","params":{{paramsJson}}}""";
		return (await Dispatcher.HandleTextAsync(request, transport, CancellationToken.None))!;
	}

	public async Task<JsonNode> ResultAsync(string method, string paramsJson, TdcpTransport transport = TdcpTransport.Relay)
	{
		var response = await CallAsync(method, paramsJson, transport);
		if (response["error"] != null)
			throw new InvalidOperationException("Unexpected error response: " + response["error"]);

		return response["result"]!;
	}

	public async Task<string> ErrorAsync(string method, string paramsJson, TdcpTransport transport = TdcpTransport.Relay)
	{
		var response = await CallAsync(method, paramsJson, transport);
		return response["error"]?["message"]?.GetValue<string>() ?? throw new InvalidOperationException("Expected an error response: " + response);
	}

	/// <summary>Calls the MCP tool as the session itself.</summary>
	public Task<JsonNode> PostAsync(JsonObject item, SessionNodeModel? caller = null) =>
		Mcp.HandleToolCallAsync(
			new JsonObject
			{
				["name"] = "post_attention",
				["arguments"] = new JsonObject { ["items"] = new JsonArray(item) },
			},
			caller ?? Session);

	public static JsonObject EntryRef(string quote, string? session = null, string? occurrence = null, string? label = null)
	{
		var r = new JsonObject { ["kind"] = "entry", ["quote"] = quote };
		if (session != null)
			r["session"] = session;
		if (occurrence != null)
			r["occurrence"] = occurrence;
		if (label != null)
			r["label"] = label;
		return r;
	}

	public static JsonObject FileRef(string path, string? label = null)
	{
		var r = new JsonObject { ["kind"] = "file", ["path"] = path };
		if (label != null)
			r["label"] = label;
		return r;
	}

	public static JsonObject ItemWithRefs(string title, params JsonObject[] refs) => new()
	{
		["kind"]  = "ask",
		["title"] = title,
		["body"]  = "see the references",
		["refs"]  = new JsonArray(Array.ConvertAll(refs, r => (JsonNode?)r)),
	};

	public void Dispose()
	{
		Base.Dispose();
	}
}
