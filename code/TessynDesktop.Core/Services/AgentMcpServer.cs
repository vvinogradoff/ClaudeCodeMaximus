using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Models.Agent;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Loopback HTTP server implementing the MCP JSON-RPC 2.0 protocol.
/// Exposes scheduling (FR.14) and orchestration (FR.15) tools to claude processes.
/// Runs on a ThreadPool background thread; all tree mutations are marshalled to the UI thread.
/// NOTE: --mcp-config HTTP transport support in the claude CLI should be verified via a spike
/// before shipping (see docs/shell_commands.md).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AgentMcpServer : IAgentMcpServer
{
	private static readonly ILogger _log = Log.ForContext<AgentMcpServer>();

	private readonly IAppSettingsService       _appSettings;
	private readonly Lazy<ISessionTurnService> _turnService;
	private readonly Lazy<ISchedulerService>   _scheduler;
	private readonly ISessionRegistry          _registry;
	private readonly ISessionFileService       _fileService;
	private readonly IClaudeModelService       _modelService;
	private readonly TdcpServer                _tdcpServer;
	private readonly IMcpConfigMerger          _configMerger;
	private readonly ITelegramBridgeService    _telegramBridge;
	private readonly INotificationService      _notifications;
	private readonly IDirectoryLabelService    _labelService;
	private readonly IAttentionService         _attention;
	private readonly ISessionTagService        _tags;
	private readonly DiagnosticsToolHandler?   _diagnostics;
	private readonly IFocusRefResolver?        _refs;
	private readonly SessionListFormatter      _listFormatter = new();
	private readonly SessionListText           _text          = new();
	private readonly SessionTagSearchPageBuilder _tagSearchBuilder   = new();
	private readonly SessionTagSearchFormatter   _tagSearchFormatter = new();
	private readonly SessionTagIndexProvider     _tagIndex;

	// Enforced concurrent worker cap (FR.15.5).
	private readonly SemaphoreSlim _workerSemaphore =
		new(Constants.Agent.MaxConcurrentWorkers, Constants.Agent.MaxConcurrentWorkers);

	// In-memory orchestration budgets keyed by "{supervisorNodeId}/{orchestrationId}" (FR.15.6).
	private readonly ConcurrentDictionary<string, BudgetEntry> _budgets = new();

	private HttpListener?            _listener;
	private CancellationTokenSource? _cts;
	private int                      _port;

	public int Port => _port;

	public AgentMcpServer(
		IAppSettingsService appSettings,
		Lazy<ISessionTurnService> turnService,
		Lazy<ISchedulerService> scheduler,
		ISessionRegistry registry,
		ISessionFileService fileService,
		IClaudeModelService modelService,
		TdcpServer tdcpServer,
		IMcpConfigMerger configMerger,
		ITelegramBridgeService telegramBridge,
		INotificationService notifications,
		IDirectoryLabelService labelService,
		IAttentionService attention,
		ISessionTagService tags,
		DiagnosticsToolHandler? diagnostics = null,
		IFocusRefResolver? refResolver = null)
	{
		_appSettings  = appSettings;
		_turnService  = turnService;
		_scheduler    = scheduler;
		_registry     = registry;
		_fileService  = fileService;
		_modelService = modelService;
		_tdcpServer   = tdcpServer;
		_configMerger = configMerger;
		_telegramBridge = telegramBridge;
		_notifications  = notifications;
		_labelService   = labelService;
		_attention      = attention;
		_tags           = tags;
		_tagIndex       = new SessionTagIndexProvider(registry, tags);
		_diagnostics    = diagnostics;
		_refs           = refResolver;
	}

	public void Start()
	{
		if (!_appSettings.Settings.AgentToolsEnabled)
			return;
		if (_listener != null)
			return;

		// Bind to a free port; save it so per-node config files can reference it.
		_port = _appSettings.Settings.AgentMcpPort > 0
			? _appSettings.Settings.AgentMcpPort
			: FindFreePort();

		try
		{
			_listener = new HttpListener();
			_listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
			_listener.Start();
		}
		catch (Exception ex)
		{
			_log.Error(ex, "AgentMcpServer: failed to start listener on port {Port}", _port);
			_listener = null;
			return;
		}

		// Persist the actual port so config files survive restarts on the same port.
		if (_appSettings.Settings.AgentMcpPort != _port)
		{
			_appSettings.Settings.AgentMcpPort = _port;
			_appSettings.Save();
		}

		_cts = new CancellationTokenSource();
		_ = AcceptLoopAsync(_cts.Token);

		_log.Information("AgentMcpServer started on port {Port}", _port);
	}

	public void Stop()
	{
		_tdcpServer.Stop();
		_cts?.Cancel();
		try { _listener?.Stop(); } catch { /* best effort */ }
		_listener = null;
		_log.Information("AgentMcpServer stopped");
	}

	public async Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken)
	{
		// A blank nodeId/agentToken (session created before identity backfill, or a latent gap
		// in a creation path) would still produce a non-null config path whose token header is
		// empty — HandleRequestAsync rejects that on the very first handshake, which the CLI
		// then reports as mcp_servers:[{"status":"failed"}] with zero cmx tools, no error surfaced.
		if (!_appSettings.Settings.AgentToolsEnabled || _port == 0
		    || string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(agentToken))
			return null;

		var dir  = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			Constants.AppDataFolderName,
			Constants.Agent.McpConfigFolderName);
		Directory.CreateDirectory(dir);

		var path    = Path.Combine(dir, $"{nodeId}.json");
		var content = await BuildMcpConfigJsonAsync(agentToken, includeCmx: true, externalServerIds: null);

		// Re-write if missing or the port/token/registry contents changed.
		if (!File.Exists(path) || File.ReadAllText(path) != content)
			File.WriteAllText(path, content, Encoding.UTF8);

		return path;
	}

	public async Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken, bool includeCmx, IReadOnlyList<string> externalServerIds)
	{
		// The cmx server itself still needs the same preconditions as the full-config path (a live
		// listener plus a valid node identity to authenticate the handshake) — silently drop it
		// rather than emit a config entry that will immediately fail (see EnsureConfigFileAsync above).
		var effectiveIncludeCmx = includeCmx && _appSettings.Settings.AgentToolsEnabled && _port != 0
		                          && !string.IsNullOrEmpty(nodeId) && !string.IsNullOrEmpty(agentToken);

		if (!effectiveIncludeCmx && externalServerIds.Count == 0)
			return null; // nothing selected — caller omits --mcp-config entirely

		var dir = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			Constants.AppDataFolderName,
			Constants.Agent.McpConfigFolderName);
		Directory.CreateDirectory(dir);

		var path    = Path.Combine(dir, $"{nodeId}.local.json");
		var content = await BuildMcpConfigJsonAsync(agentToken, effectiveIncludeCmx, externalServerIds);

		if (!File.Exists(path) || File.ReadAllText(path) != content)
			File.WriteAllText(path, content, Encoding.UTF8);

		return path;
	}

	// ── Private helpers ───────────────────────────────────────────────────────

	/// <summary>
	/// Pins the supervisor's resolved profile onto a freshly-spawned child so its headless turns
	/// authenticate with the same account. Without this the child has no file history to resolve a
	/// profile from, falls back to the Default profile, and every turn 401s if Default's OAuth token
	/// is revoked (see docs/issues/ISSUE-002-orchestrated-child-profile-inheritance.md).
	/// </summary>
	private void InheritSupervisorProfile(SessionNodeModel child, SessionNodeModel supervisor)
	{
		if (!string.IsNullOrEmpty(child.ProfileName))
			return; // already pinned

		var profileName = _turnService.Value.ResolveProfileName(supervisor);
		if (!string.IsNullOrEmpty(profileName))
			child.ProfileName = profileName;
	}

	/// <summary>
	/// Builds the --mcp-config content: the cmx server (FR.14.1, when <paramref name="includeCmx"/>)
	/// plus External MCP Server Registry entries (FR.20.2), merged into one mcpServers map.
	/// <paramref name="externalServerIds"/> null means "every enabled entry" (the full-config path
	/// used for Anthropic sessions); a non-null list restricts to just those IDs (the Local Tool
	/// Profile path, FR.12.21).
	/// </summary>
	private async Task<string> BuildMcpConfigJsonAsync(string agentToken, bool includeCmx, IReadOnlyList<string>? externalServerIds)
	{
		var registryServers = await _configMerger.BuildRegistryServersAsync(_appSettings.Settings.ExternalMcpServers);
		return BuildMcpConfigJson(includeCmx, _port, agentToken, registryServers, externalServerIds);
	}

	/// <summary>
	/// Pure merge/serialize step of <see cref="BuildMcpConfigJsonAsync"/>, extracted so
	/// <c>TessynDesktop.Tests</c> can assert on the filtering behavior (FR.12.21) without
	/// constructing a full <see cref="AgentMcpServer"/> (HTTP listener, registry, scheduler, etc.).
	/// </summary>
	internal static string BuildMcpConfigJson(
		bool includeCmx, int port, string agentToken,
		IReadOnlyDictionary<string, object> registryServers, IReadOnlyList<string>? externalServerIds)
	{
		var mcpServers = new Dictionary<string, object>();

		// "type":"http" is required by the claude CLI to recognise this as an HTTP MCP server;
		// without it the entry is silently ignored (mcp_servers:[]).
		// The "headers" object is forwarded on every request so the server can map the token to the calling node.
		if (includeCmx)
		{
			mcpServers[Constants.Agent.McpServerName] = new
			{
				type    = "http",
				url     = $"http://127.0.0.1:{port}{Constants.Agent.McpEndpointPath}",
				headers = new Dictionary<string, string>
				{
					[Constants.Agent.TokenHeader] = agentToken,
				},
			};
		}

		foreach (var (id, server) in registryServers)
		{
			if (externalServerIds == null || externalServerIds.Contains(id))
				mcpServers[id] = server;
		}

		return JsonSerializer.Serialize(new { mcpServers }, new JsonSerializerOptions { WriteIndented = true });
	}

	private async Task AcceptLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				var ctx = await _listener!.GetContextAsync();
				_ = HandleRequestAsync(ctx);
			}
			catch (HttpListenerException) when (ct.IsCancellationRequested)
			{
				break;
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "AgentMcpServer: error in accept loop");
			}
		}
	}

	private async Task HandleRequestAsync(HttpListenerContext ctx)
	{
		try
		{
			// Route TDCP requests to TdcpServer (/tdcp); leave /mcp flow byte-for-byte unchanged.
			if (ctx.Request.Url?.AbsolutePath == Constants.Agent.TdcpEndpointPath)
			{
				await _tdcpServer.HandleAsync(ctx);
				return;
			}

			if (ctx.Request.HttpMethod != "POST"
			    || !ctx.Request.RawUrl?.StartsWith(Constants.Agent.McpEndpointPath) == true)
			{
				ctx.Response.StatusCode = 404;
				ctx.Response.Close();
				return;
			}

			var token = ctx.Request.Headers[Constants.Agent.TokenHeader];
			if (string.IsNullOrEmpty(token))
			{
				await SendErrorAsync(ctx, -32600, "Missing X-CMX-Token header");
				return;
			}

			// Resolve caller node from the registry (thread-safe, no UI dispatch needed).
			var callerNode = _registry.FindModelByAgentToken(token);

			if (callerNode == null)
			{
				await SendErrorAsync(ctx, -32600, "Unknown agent token");
				return;
			}

			using var reader  = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
			var body          = await reader.ReadToEndAsync();
			JsonNode? request;
			try
			{
				request = JsonNode.Parse(body);
			}
			catch
			{
				await SendErrorAsync(ctx, -32700, "Parse error");
				return;
			}

			var id     = request?["id"];
			var method = request?["method"]?.GetValue<string>();
			var @params = request?["params"];

			JsonNode result;
			try
			{
				result = method switch
				{
					"initialize"  => HandleInitialize(),
					"tools/list"  => HandleToolsList(),
					"tools/call"  => await HandleToolCallAsync(@params, callerNode),
					_             => throw new McpMethodNotFoundException(method ?? ""),
				};
			}
			catch (McpMethodNotFoundException ex)
			{
				await SendJsonRpcErrorAsync(ctx, id, -32601, $"Method not found: {ex.Message}");
				return;
			}
			catch (McpToolException ex)
			{
				// Tool returned an application-level error → encode as MCP error content.
				result = BuildTextContent($"Error: {ex.Message}");
			}

			await SendJsonRpcResultAsync(ctx, id, result);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "AgentMcpServer: unhandled error handling request");
			try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* best effort */ }
		}
	}

	// ── MCP method handlers ───────────────────────────────────────────────────

	private static JsonNode HandleInitialize() =>
		JsonNode.Parse($@"{{
			""protocolVersion"": ""{Constants.Agent.ProtocolVersion}"",
			""capabilities"": {{ ""tools"": {{}} }},
			""serverInfo"": {{ ""name"": ""{Constants.Agent.McpServerName}"", ""version"": ""1.0"" }}
		}}")!;

	private static JsonNode HandleToolsList()
	{
		var tools = new JsonArray(
			BuildToolDef("schedule_wake",
				"Schedule a future turn for a session. Omit 'target' to wake the current session (self-wake).",
				ScheduleWakeSchema()),
			BuildToolDef("list_schedules",
				"List schedules. Pass all=true to see all app schedules; otherwise returns caller's schedules.",
				ListSchedulesSchema()),
			BuildToolDef("cancel_schedule",
				"Cancel a schedule by its ID.",
				CancelScheduleSchema()),
			BuildToolDef("list_sessions",
				"Find sessions. With no arguments: your project's top-level sessions plus you and your own workers (nested under you), newest first, 30 rows per page. Each row: <id> <name> <age> [flags]; flags: running, fav, no-resume (next turn starts a fresh Claude session seeded from the transcript), N workers, worker of <id>, [other directory], (you). Narrow with query/running/activeWithinHours, filter by tags=[...] (see search_tags), widen with scope=all, see a session's workers with scope=children|subtree nodeId=<id>, page with offset. Short ids work wherever a session id is taken.",
				ListSessionsSchema()),
			BuildToolDef("spawn_session",
				"Create a new persistent session node and optionally run the first turn. Model is tier-enforced (FR.16).",
				SpawnSessionSchema()),
			BuildToolDef("send_to_session",
				"Resume an existing session and run a turn. Use mode=async to not block. Supports an optional markdown outputFormat hint.",
				SendToSessionSchema()),
			BuildToolDef("read_session",
				"Read the last N entries from a session's file.",
				ReadSessionSchema()),
			BuildToolDef("stop_session",
				"Cancel the currently running turn for a session.",
				StopSessionSchema()),
			BuildToolDef("set_session_model",
				"Change the model for an existing session. Tier-enforced (FR.16.2). Takes effect on next turn.",
				SetSessionModelSchema()),
			BuildToolDef("orchestrate_parallel",
				"Barrier fan-out: spawns one session per task, runs all concurrently, returns all results as one markdown document when done (FR.15.8).",
				OrchestrateParallelSchema()),
			BuildToolDef("orchestrate_pipeline",
				"Item pipeline: one session per item through sequential stages, all items run concurrently, returns all results as one markdown document (FR.15.8).",
				OrchestratePipelineSchema()),
			BuildToolDef("workflow_phase",
				"Write a [Phase: title] progress marker to the supervisor session file (FR.15.9).",
				WorkflowPhaseSchema()),
			BuildToolDef("workflow_log",
				"Write a [Log: message] progress entry to the supervisor session file (FR.15.9).",
				WorkflowLogSchema()),
			BuildToolDef("get_budget",
				"Return token usage and remaining budget for the caller's active orchestrations (FR.15.6).",
				EmptyObjectSchema()),
			BuildToolDef("notify_user",
				"Send the user a notification or plain-text question — to their Telegram (FR.21.3) and as a desktop notification (FR.21.5) — only for the top-level session the user is talking to. Never call from a worker session (its own call is dropped outright, FR.21.19) or a Task-tool subagent — report back to your supervisor instead. Use only when the user must personally act or decide; one message per need, not progress updates. Fire-and-forget — there is no blocking 'ask and wait' tool; end your turn after calling this and the user's reply will arrive as your next prompt.",
				NotifyUserSchema()),
			BuildToolDef(Constants.Attention.PostToolName,
				Constants.Attention.PostToolDescription,
				PostAttentionSchema()),
			BuildToolDef(Constants.Attention.ResolveToolName,
				Constants.Attention.ResolveToolDescription,
				ResolveAttentionSchema()),
			BuildToolDef(Constants.Attention.ListToolName,
				Constants.Attention.ListToolDescription,
				ListAttentionSchema()),
			BuildToolDef(Constants.SessionTags.ToolName,
				Constants.SessionTags.ToolDescription,
				TagSessionSchema()),
			BuildToolDef(Constants.SessionTags.SearchToolName,
				Constants.SessionTags.SearchToolDescription,
				SearchTagsSchema()),
			BuildToolDef(Constants.Diagnostics.SnapshotToolName,
				Constants.Diagnostics.SnapshotToolDescription,
				EmptyObjectSchema()),
			BuildToolDef(Constants.Diagnostics.HeapToolName,
				Constants.Diagnostics.HeapToolDescription,
				EmptyObjectSchema()),
			BuildToolDef(Constants.Diagnostics.DumpToolName,
				Constants.Diagnostics.DumpToolDescription,
				EmptyObjectSchema()));

		return new JsonObject { ["tools"] = tools };
	}

	internal async Task<JsonNode> HandleToolCallAsync(JsonNode? @params, SessionNodeModel callerNode)
	{
		var name      = @params?["name"]?.GetValue<string>()
		                ?? throw new McpToolException("Missing tool name");
		var arguments = @params?["arguments"];

		return name switch
		{
			"schedule_wake"        => await ScheduleWakeAsync(arguments, callerNode),
			"list_schedules"       => ListSchedules(arguments, callerNode),
			"cancel_schedule"      => CancelSchedule(arguments),
			"list_sessions"        => ListSessions(arguments, callerNode),
			"spawn_session"        => await SpawnSessionAsync(arguments, callerNode),
			"send_to_session"      => await SendToSessionAsync(arguments, callerNode),
			"read_session"         => await ReadSessionAsync(arguments),
			"stop_session"         => StopSession(arguments),
			"set_session_model"    => await SetSessionModelAsync(arguments, callerNode),
			"orchestrate_parallel" => await OrchestrateParallelAsync(arguments, callerNode),
			"orchestrate_pipeline" => await OrchestratePipelineAsync(arguments, callerNode),
			"workflow_phase"       => WorkflowPhase(arguments, callerNode),
			"workflow_log"         => WorkflowLog(arguments, callerNode),
			"get_budget"           => GetBudget(callerNode),
			"notify_user"          => await NotifyUserAsync(arguments, callerNode),
			Constants.Attention.PostToolName    => await PostAttentionAsync(arguments, callerNode),
			Constants.Attention.ResolveToolName => ResolveAttention(arguments, callerNode),
			Constants.Attention.ListToolName    => ListAttention(arguments, callerNode),
			Constants.SessionTags.ToolName      => TagSession(arguments, callerNode),
			Constants.SessionTags.SearchToolName => SearchTags(arguments, callerNode),
			Constants.Diagnostics.SnapshotToolName => RunDiagnosticsTool(h => h.Snapshot()),
			Constants.Diagnostics.HeapToolName     => RunDiagnosticsTool(h => h.Heap()),
			Constants.Diagnostics.DumpToolName     => RunDiagnosticsTool(h => h.Dump()),
			_                      => throw new McpMethodNotFoundException(name),
		};
	}

	// ── Diagnostics tools (D1.7) ──────────────────────────────────────────────

	/// <summary>Runs on the HttpListener thread; must never marshal to the UI thread, so it answers while the UI is frozen.</summary>
	private JsonNode RunDiagnosticsTool(Func<DiagnosticsToolHandler, DiagnosticToolResultModel> run)
	{
		if (_diagnostics == null)
			throw new McpToolException("Diagnostics are not available in this host");
		var result = run(_diagnostics);
		return result.IsError ? throw new McpToolException(result.Text) : BuildTextContent(result.Text);
	}

	// ── Scheduling tools ──────────────────────────────────────────────────────

	private async Task<JsonNode> ScheduleWakeAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var when   = args?["when"] ?? throw new McpToolException("'when' is required");
		var prompt = args?["prompt"]?.GetValue<string>() ?? string.Empty;
		var note   = args?["note"]?.GetValue<string>() ?? string.Empty;
		var target = args?["target"]?.GetValue<string>();

		// Self-wake: no target → use caller's own NodeId; otherwise resolve the full id or unique prefix.
		var targetNode = string.IsNullOrWhiteSpace(target)
			? _registry.FindModelByNodeId(callerNode.NodeId)
			: ResolveSession(target, "Target session not found");

		if (targetNode == null)
			throw new McpToolException($"Target session not found: {callerNode.NodeId}");

		var targetNodeId = targetNode.NodeId;

		var scheduleId = Guid.NewGuid().ToString("N")[..8];

		ScheduleModel schedule;
		if (when["inSeconds"] is not null)
		{
			var seconds   = when["inSeconds"]!.GetValue<double>();
			schedule = new ScheduleModel
			{
				ScheduleId   = scheduleId,
				TargetNodeId = targetNodeId,
				Prompt       = prompt,
				Note         = note,
				Kind         = ScheduleKind.Delay,
				FireAtUtc    = DateTimeOffset.UtcNow.AddSeconds(seconds),
			};
		}
		else if (when["at"] is not null)
		{
			var at = DateTimeOffset.Parse(when["at"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind);
			schedule = new ScheduleModel
			{
				ScheduleId   = scheduleId,
				TargetNodeId = targetNodeId,
				Prompt       = prompt,
				Note         = note,
				Kind         = ScheduleKind.At,
				FireAtUtc    = at,
			};
		}
		else if (when["cron"] is not null)
		{
			var cron = when["cron"]!.GetValue<string>();
			schedule = new ScheduleModel
			{
				ScheduleId     = scheduleId,
				TargetNodeId   = targetNodeId,
				Prompt         = prompt,
				Note           = note,
				Kind           = ScheduleKind.Cron,
				CronExpression = cron,
				MaxFires       = Constants.Agent.MaxTurnsPerLoop,
			};
		}
		else
			throw new McpToolException("'when' must contain 'inSeconds', 'at', or 'cron'");

		_scheduler.Value.AddSchedule(schedule);

		var fireDesc = schedule.Kind == ScheduleKind.Cron
			? $"cron '{schedule.CronExpression}'"
			: $"at {schedule.FireAtUtc:u}";
		return BuildTextContent($"Scheduled (id={scheduleId}) {fireDesc}" +
		                        (targetNodeId == callerNode.NodeId ? " [self]" : $" → {targetNodeId}"));
	}

	private JsonNode ListSchedules(JsonNode? args, SessionNodeModel callerNode)
	{
		var all       = args?["all"]?.GetValue<bool>() ?? false;
		var schedules = _scheduler.Value.GetSchedules(all ? null : callerNode.NodeId);

		var lines = schedules.Select(s =>
		{
			var when = s.Kind == ScheduleKind.Cron ? s.CronExpression : s.FireAtUtc?.ToString("u");
			return $"[{s.ScheduleId}] {s.Kind} {when} → {s.TargetNodeId}" +
			       (string.IsNullOrEmpty(s.Note) ? "" : $" ({s.Note})");
		});

		return BuildTextContent(schedules.Count == 0
			? "No schedules."
			: string.Join("\n", lines));
	}

	private JsonNode CancelSchedule(JsonNode? args)
	{
		var scheduleId = args?["scheduleId"]?.GetValue<string>()
		                 ?? throw new McpToolException("'scheduleId' is required");
		var removed = _scheduler.Value.RemoveSchedule(scheduleId);
		return BuildTextContent(removed ? $"Cancelled {scheduleId}." : $"Schedule not found: {scheduleId}");
	}

	// ── Orchestration tools ───────────────────────────────────────────────────

	private JsonNode ListSessions(JsonNode? args, SessionNodeModel callerNode)
	{
		var scope = ParseScope(GetOptionalString(args, "scope"),
			SessionListScope.Project, SessionListScope.All, SessionListScope.Children, SessionListScope.Subtree);

		var nodeIdArg         = GetOptionalString(args, "nodeId");
		var query             = GetOptionalString(args, "query");
		var running           = GetOptionalBool(args, "running");
		var activeWithinHours = GetOptionalInt(args, "activeWithinHours");
		var includeWorkers    = GetOptionalBool(args, "includeWorkers") ?? false;
		var limit             = GetOptionalInt(args, "limit");
		var offset            = GetOptionalInt(args, "offset");

		if (activeWithinHours is <= 0)
			throw new McpToolException("'activeWithinHours' must be greater than 0.");

		var tagCarriers = ResolveTagCarriers(args, scope, callerNode);

		string? rootNodeId = null;
		if (scope is SessionListScope.Children or SessionListScope.Subtree && !string.IsNullOrWhiteSpace(nodeIdArg))
			rootNodeId = ResolveSession(nodeIdArg, "Session not found").NodeId;

		var listQuery = new SessionListQueryModel
		{
			Scope             = scope,
			RootNodeId        = rootNodeId,
			Query             = string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
			Running           = running,
			ActiveWithinHours = activeWithinHours,
			IncludeWorkers    = includeWorkers,
			TagCarrierNodeIds = tagCarriers,
			Limit             = limit ?? Constants.Agent.ListSessionsDefaultLimit,
			Offset            = offset ?? 0,
		};

		var page = _registry.QueryAgentSessions(listQuery, callerNode.NodeId);
		return BuildTextContent(_listFormatter.Format(page, DateTimeOffset.UtcNow));
	}

	/// <summary>
	/// <c>list_sessions</c> <c>tags</c> (FR.15.13, FR.24.15): the entries become canonical keys through
	/// <see cref="ISessionTagService.Parse"/> (nothing learned or persisted), then the top-level sessions carrying any
	/// of them. Null = no filter. The index covers the caller's Directory node in <c>project</c> scope, every
	/// directory otherwise; tag files are read outside the registry lock.
	/// </summary>
	private IReadOnlySet<string>? ResolveTagCarriers(JsonNode? args, SessionListScope scope, SessionNodeModel callerNode)
	{
		var entries = ReadStringArray(args, "tags");
		if (entries.Count > Constants.SessionTags.MaxEntriesPerArray)
			throw new McpToolException($"'tags' may contain at most {Constants.SessionTags.MaxEntriesPerArray} entries.");

		var keys = new List<string>();
		foreach (var entry in entries.Select(e => e.Trim()).Where(e => e.Length > 0))
		{
			var parse = _tags.Parse(entry);
			keys.Add(parse.Outcome switch
			{
				SessionTagParseOutcome.Valid       => parse.Tag!.Key,
				SessionTagParseOutcome.UnknownTeam => parse.UnknownIssueKey!,
				_ => throw new McpToolException(string.Format(Constants.SessionTags.NotATagErrorFormat, entry)),
			});
		}

		if (keys.Count == 0)
			return null;

		var snapshot  = _registry.SnapshotSessionsForTagIndex();
		string? directoryPath = null;
		if (scope == SessionListScope.Project)
		{
			directoryPath = snapshot.FirstOrDefault(s => string.Equals(s.NodeId, callerNode.NodeId, StringComparison.Ordinal))?.DirectoryPath;
			if (directoryPath == null)
				return new HashSet<string>(StringComparer.Ordinal);
		}

		return SessionTagSearch.SessionsWithAnyTag(_tagIndex.Build(snapshot, directoryPath), keys);
	}

	/// <summary><c>search_tags</c> (FR.24.15): one bounded page of the tags in the caller's project or in every directory.</summary>
	private JsonNode SearchTags(JsonNode? args, SessionNodeModel callerNode)
	{
		var scope  = ParseTagSearchScope(GetOptionalString(args, "scope"));
		var query  = GetOptionalString(args, "query");
		var limit  = GetOptionalInt(args, "limit");
		var offset = GetOptionalInt(args, "offset");

		var searchQuery = new SessionTagSearchQueryModel
		{
			Query  = query,
			Scope  = scope,
			Limit  = limit ?? Constants.Agent.ListSessionsDefaultLimit,
			Offset = offset ?? 0,
		};

		var page = _tagSearchBuilder.Build(
			_registry.SnapshotSessionsForListing(),
			callerNode.NodeId,
			searchQuery,
			_tagIndex.Build,
			s => _fileService.GetLastWriteTimeUtc(s.FileName));

		return BuildTextContent(_tagSearchFormatter.Format(page));
	}

	private static SessionListScope ParseTagSearchScope(string? scopeText) =>
		ParseScope(scopeText, SessionListScope.Project, SessionListScope.All);

	/// <summary>Parses a scope name (case-insensitive, trimmed); blank = project. Only <paramref name="allowed"/> names are accepted.</summary>
	private static SessionListScope ParseScope(string? scopeText, params SessionListScope[] allowed)
	{
		if (string.IsNullOrWhiteSpace(scopeText))
			return SessionListScope.Project;

		var name  = scopeText.Trim();
		var match = allowed.Where(a => string.Equals(a.ToString(), name, StringComparison.OrdinalIgnoreCase)).ToList();
		if (match.Count > 0)
			return match[0];

		var names = allowed.Select(a => a.ToString().ToLowerInvariant()).ToList();
		var list  = names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " or " + names[^1];
		throw new McpToolException($"Unknown scope '{scopeText}'. Use {list}.");
	}

	/// <summary>
	/// Resolves a full NodeId or a unique prefix (FR.15.13) to its session; throws a tool error for
	/// too-short, ambiguous and unknown ids. <paramref name="notFoundText"/> is the tool's not-found wording.
	/// </summary>
	private SessionNodeModel ResolveSession(string idOrPrefix, string notFoundText)
	{
		var resolution = ResolveNodeIdOrThrow(idOrPrefix, out var id);
		var model      = resolution.Status == NodeIdResolutionStatus.Resolved && resolution.NodeId != null
			? _registry.FindModelByNodeId(resolution.NodeId)
			: null;

		return model ?? throw new McpToolException($"{notFoundText}: {id}");
	}

	/// <summary>
	/// Trims <paramref name="idOrPrefix"/> and resolves it; throws for too-short and ambiguous ids and
	/// returns Resolved or NotFound otherwise.
	/// </summary>
	private NodeIdResolutionModel ResolveNodeIdOrThrow(string idOrPrefix, out string id)
	{
		id = idOrPrefix.Trim();
		var resolution = _registry.ResolveNodeId(id);

		if (resolution.Status == NodeIdResolutionStatus.TooShort)
			throw new McpToolException(
				$"Session id '{id}' is too short: use at least {Constants.Agent.NodeIdPrefixMinLength} characters.");

		if (resolution.Status == NodeIdResolutionStatus.Ambiguous)
		{
			var matches = string.Join(", ", resolution.Matches.Select(m => $"{m.NodeId} \"{_text.FormatName(m.Name)}\""));
			var more    = resolution.MatchCount > resolution.Matches.Count ? ", ..." : string.Empty;
			throw new McpToolException(
				$"Session id '{id}' matches {resolution.MatchCount} sessions: {matches}{more}. Use more characters.");
		}

		return resolution;
	}

	private static string? GetOptionalString(JsonNode? args, string name)
	{
		var node = args?[name];
		if (node == null)
			return null;
		if (node is JsonValue value && value.TryGetValue<string>(out var text))
			return text;
		throw new McpToolException($"'{name}' must be a string.");
	}

	private static bool? GetOptionalBool(JsonNode? args, string name)
	{
		var node = args?[name];
		if (node == null)
			return null;
		if (node is JsonValue value && value.TryGetValue<bool>(out var flag))
			return flag;
		throw new McpToolException($"'{name}' must be a boolean.");
	}

	private static int? GetOptionalInt(JsonNode? args, string name)
	{
		var node = args?[name];
		if (node == null)
			return null;
		if (node is JsonValue value)
		{
			if (value.TryGetValue<int>(out var number))
				return number;
			if (value.TryGetValue<double>(out var real) && real == Math.Floor(real) && Math.Abs(real) <= int.MaxValue)
				return (int)real;
		}
		throw new McpToolException($"'{name}' must be an integer.");
	}

	private async Task<JsonNode> SpawnSessionAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var name         = args?["name"]?.GetValue<string>()
		                   ?? throw new McpToolException("'name' is required");
		var prompt       = args?["prompt"]?.GetValue<string>() ?? string.Empty;
		var group        = args?["group"]?.GetValue<string>();
		var modelArg     = args?["model"]?.GetValue<string>();
		var outputFormat = args?["outputFormat"]?.GetValue<string>();

		// Tier enforcement (FR.16.2).
		if (!string.IsNullOrEmpty(modelArg))
			EnforceModelTier(callerNode, modelArg);

		// Depth enforcement (FR.15.5).
		var childDepth = callerNode.OrchestrationDepth + 1;
		if (childDepth > Constants.Agent.MaxOrchestrationDepth)
			throw new McpToolException(
				$"Orchestration depth limit exceeded (max {Constants.Agent.MaxOrchestrationDepth}). " +
				$"This session is at depth {callerNode.OrchestrationDepth}.");

		// Resolve working directory.
		string workingDir;
		var explicitDir     = args?["workingDir"]?.GetValue<string>();
		var parentNodeIdArg = args?["parentNodeId"]?.GetValue<string>();

		if (!string.IsNullOrEmpty(explicitDir))
			workingDir = explicitDir;
		else if (!string.IsNullOrEmpty(parentNodeIdArg))
		{
			var parentModel = ResolveSession(parentNodeIdArg, "Parent node not found");
			workingDir = parentModel.WorkingDirectory;
		}
		else
			workingDir = callerNode.WorkingDirectory;

		// Create the node via the registry (thread-safe, raises TreeChanged so VM re-projects).
		var newModel = _registry.CreateSessionForAgent(workingDir, name, group, childDepth, callerNode.NodeId);

		if (newModel == null)
			throw new McpToolException($"Could not create session under working dir: {workingDir}");

		// Inherit the supervisor's profile so the child authenticates with the same account (ISSUE-002).
		InheritSupervisorProfile(newModel, callerNode);

		// Persist model on the new session (FR.16.3).
		if (!string.IsNullOrEmpty(modelArg))
			newModel.ModelId = modelArg;

		_appSettings.Save();

		// Run first turn if a prompt was supplied.
		TurnResultModel? result = null;
		if (!string.IsNullOrEmpty(prompt))
			result = await _turnService.Value.RunTurnAsync(
				newModel, ApplyOutputFormat(prompt, outputFormat), TurnSource.Orchestrated);

		return BuildTextContent(result == null
			? $"Session '{name}' created (nodeId={newModel.NodeId}). No prompt run."
			: $"Session '{name}' created (nodeId={newModel.NodeId}). Result:\n{result.AssistantText}");
	}

	private async Task<JsonNode> SendToSessionAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var targetNodeId = args?["nodeId"]?.GetValue<string>()
		                   ?? throw new McpToolException("'nodeId' is required");
		var prompt       = args?["prompt"]?.GetValue<string>()
		                   ?? throw new McpToolException("'prompt' is required");
		var mode         = args?["mode"]?.GetValue<string>() ?? "wait";
		var outputFormat = args?["outputFormat"]?.GetValue<string>();

		var targetModel = ResolveSession(targetNodeId, "Session not found");
		targetNodeId    = targetModel.NodeId;

		var augmentedPrompt = ApplyOutputFormat(prompt, outputFormat);

		if (mode == "async")
		{
			var callerSnapshot = callerNode;
			var targetSnapshot = targetModel;
			_ = Task.Run(async () =>
			{
				try
				{
					var result = await _turnService.Value.RunTurnAsync(
						targetSnapshot, augmentedPrompt, TurnSource.Orchestrated);
					_scheduler.Value.AddOrMergeMailboxDelivery(
						callerSnapshot.NodeId, targetSnapshot.Name, result.AssistantText);
				}
				catch (Exception ex)
				{
					_log.Warning(ex, "Async send_to_session failed for node {NodeId}", targetNodeId);
				}
			});
			return BuildTextContent($"Sent async to '{targetModel.Name}' ({targetNodeId}). Result will be posted back.");
		}
		else
		{
			var result = await _turnService.Value.RunTurnAsync(targetModel, augmentedPrompt, TurnSource.Orchestrated);
			return BuildTextContent(result.IsError
				? $"Error: {result.ErrorMessage}"
				: result.AssistantText);
		}
	}

	private Task<JsonNode> ReadSessionAsync(JsonNode? args)
	{
		var nodeId = args?["nodeId"]?.GetValue<string>()
		             ?? throw new McpToolException("'nodeId' is required");
		var lastN  = args?["lastN"]?.GetValue<int>() ?? 20;

		var model = ResolveSession(nodeId, "Session not found");

		var entries = _fileService.ReadEntries(model.FileName);
		var relevant = entries
			.Where(e => e.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant)
			.TakeLast(lastN > 0 ? lastN : int.MaxValue)
			.ToList();

		if (relevant.Count == 0)
			return Task.FromResult(BuildTextContent("Session has no conversation entries."));

		var sb = new StringBuilder();
		foreach (var e in relevant)
		{
			sb.AppendLine($"[{e.Timestamp:u}] {e.Role}");
			sb.AppendLine(e.Content);
			sb.AppendLine();
		}
		return Task.FromResult(BuildTextContent(sb.ToString().TrimEnd()));
	}

	private JsonNode StopSession(JsonNode? args)
	{
		var nodeId  = args?["nodeId"]?.GetValue<string>()
		              ?? throw new McpToolException("'nodeId' is required");
		// An id matching no session is passed on as is, so the running turn of a session that has left the tree can still be cancelled.
		var resolution = ResolveNodeIdOrThrow(nodeId, out var id);
		var targetId   = resolution.Status == NodeIdResolutionStatus.Resolved && resolution.NodeId != null ? resolution.NodeId : id;
		var stopped    = _turnService.Value.CancelTurn(targetId);
		return BuildTextContent(stopped ? $"Cancelled turn for {targetId}." : $"No active turn for {targetId}.");
	}

	// ── New orchestration tools (FR.15.6–FR.15.10, FR.16) ───────────────────

	private Task<JsonNode> SetSessionModelAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var targetNodeId = args?["nodeId"]?.GetValue<string>()
		                   ?? throw new McpToolException("'nodeId' is required");
		var model        = args?["model"]?.GetValue<string>()
		                   ?? throw new McpToolException("'model' is required");

		EnforceModelTier(callerNode, model);

		var target = ResolveSession(targetNodeId, "Session not found");
		targetNodeId = target.NodeId;

		target.ModelId = model;
		_appSettings.Save();
		return Task.FromResult(BuildTextContent($"Session '{target.Name}' ({targetNodeId}) model set to '{model}'. Effective from next turn."));
	}

	private async Task<JsonNode> OrchestrateParallelAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var tasksNode    = args?["tasks"]?.AsArray()
		                   ?? throw new McpToolException("'tasks' is required");
		var modelArg     = args?["model"]?.GetValue<string>();
		var group        = args?["group"]?.GetValue<string>() ?? "Orchestration";
		var outputFormat = args?["outputFormat"]?.GetValue<string>();
		var budgetTokens = args?["budgetTokens"]?.GetValue<int>() ?? 0;

		if (!string.IsNullOrEmpty(modelArg))
			EnforceModelTier(callerNode, modelArg);

		var childDepth = callerNode.OrchestrationDepth + 1;
		if (childDepth > Constants.Agent.MaxOrchestrationDepth)
			throw new McpToolException(
				$"Orchestration depth limit exceeded (max {Constants.Agent.MaxOrchestrationDepth}).");

		// Register budget if requested.
		var orchestrationId = Guid.NewGuid().ToString("N")[..8];
		if (budgetTokens > 0)
			_budgets[$"{callerNode.NodeId}/{orchestrationId}"] = new BudgetEntry(budgetTokens);

		// Build task list upfront to detect errors early.
		var tasks = new List<(string name, string prompt)>();
		for (var i = 0; i < tasksNode.Count; i++)
		{
			var t = tasksNode[i];
			var tName   = t?["name"]?.GetValue<string>()   ?? $"worker-{i + 1}";
			var tPrompt = t?["prompt"]?.GetValue<string>() ?? throw new McpToolException($"Task {i} missing 'prompt'");
			tasks.Add((tName, tPrompt));
		}

		// Spawn and run all tasks concurrently, bounded by MaxConcurrentWorkers.
		var taskRuns = tasks.Select((task, idx) => RunWorkerAsync(
			task.name, task.prompt, idx, callerNode, modelArg, group, childDepth,
			outputFormat, budgetTokens > 0 ? $"{callerNode.NodeId}/{orchestrationId}" : null)).ToArray();

		var results = await Task.WhenAll(taskRuns);

		// Clean up budget entry.
		_budgets.TryRemove($"{callerNode.NodeId}/{orchestrationId}", out _);

		return BuildTextContent(BuildWorkerResultsMarkdown(results));
	}

	private async Task<JsonNode> OrchestratePipelineAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var itemsNode    = args?["items"]?.AsArray()
		                   ?? throw new McpToolException("'items' is required");
		var stagesNode   = args?["stages"]?.AsArray()
		                   ?? throw new McpToolException("'stages' is required");
		var modelArg     = args?["model"]?.GetValue<string>();
		var group        = args?["group"]?.GetValue<string>() ?? "Pipeline";
		var outputFormat = args?["outputFormat"]?.GetValue<string>();
		var budgetTokens = args?["budgetTokens"]?.GetValue<int>() ?? 0;

		if (!string.IsNullOrEmpty(modelArg))
			EnforceModelTier(callerNode, modelArg);

		var childDepth = callerNode.OrchestrationDepth + 1;
		if (childDepth > Constants.Agent.MaxOrchestrationDepth)
			throw new McpToolException(
				$"Orchestration depth limit exceeded (max {Constants.Agent.MaxOrchestrationDepth}).");

		var stages = new List<string>();
		for (var i = 0; i < stagesNode.Count; i++)
		{
			var s = stagesNode[i]?["prompt"]?.GetValue<string>()
			        ?? throw new McpToolException($"Stage {i} missing 'prompt'");
			stages.Add(s);
		}

		var orchestrationId = Guid.NewGuid().ToString("N")[..8];
		if (budgetTokens > 0)
			_budgets[$"{callerNode.NodeId}/{orchestrationId}"] = new BudgetEntry(budgetTokens);

		// Run each item through all stages in its own session, all items concurrent.
		var itemRuns = itemsNode.Select((itemNode, idx) =>
		{
			var item = itemNode?.GetValue<string>() ?? $"item-{idx + 1}";
			return RunPipelineItemAsync(item, idx, stages, callerNode, modelArg, group, childDepth,
				outputFormat, budgetTokens > 0 ? $"{callerNode.NodeId}/{orchestrationId}" : null);
		}).ToArray();

		var results = await Task.WhenAll(itemRuns);
		_budgets.TryRemove($"{callerNode.NodeId}/{orchestrationId}", out _);

		return BuildTextContent(BuildPipelineResultsMarkdown(results));
	}

	/// <summary>Runs a single parallel worker task: acquire slot → spawn session → run turn → release.</summary>
	private async Task<WorkerResult> RunWorkerAsync(
		string name, string prompt, int index, SessionNodeModel callerNode,
		string? model, string group, int depth, string? outputFormat, string? budgetKey)
	{
		await _workerSemaphore.WaitAsync();
		try
		{
			if (budgetKey != null && _budgets.TryGetValue(budgetKey, out var budget) && budget.IsExhausted)
				return new WorkerResult(name, "[budget-exhausted]", 0, 0);

			var session = _registry.CreateSessionForAgent(
				callerNode.WorkingDirectory, name, group, depth, callerNode.NodeId);

			if (session == null)
				return new WorkerResult(name, "[error: could not create session]", 0, 0);

			// Inherit the supervisor's profile so the worker authenticates with the same account (ISSUE-002).
			InheritSupervisorProfile(session, callerNode);
			if (!string.IsNullOrEmpty(model))
				session.ModelId = model;

			var result = await _turnService.Value.RunTurnAsync(
				session, ApplyOutputFormat(prompt, outputFormat), TurnSource.Orchestrated);

			if (budgetKey != null)
				_budgets[budgetKey].Accrue(result.InputTokens + result.OutputTokens);

			return new WorkerResult(name, result.AssistantText, result.InputTokens, result.OutputTokens);
		}
		finally
		{
			_workerSemaphore.Release();
		}
	}

	/// <summary>Runs a single pipeline item through all stages sequentially in its own session.</summary>
	private async Task<PipelineResult> RunPipelineItemAsync(
		string item, int index, List<string> stages, SessionNodeModel callerNode,
		string? model, string group, int depth, string? outputFormat, string? budgetKey)
	{
		await _workerSemaphore.WaitAsync();
		try
		{
			if (budgetKey != null && _budgets.TryGetValue(budgetKey, out var budget) && budget.IsExhausted)
				return new PipelineResult(item, "[budget-exhausted]", 0, 0);

			var session = _registry.CreateSessionForAgent(
				callerNode.WorkingDirectory, $"pipeline-{index + 1}", group, depth, callerNode.NodeId);

			if (session == null)
				return new PipelineResult(item, "[error: could not create session]", 0, 0);

			// Inherit the supervisor's profile so the item session authenticates with the same account (ISSUE-002).
			InheritSupervisorProfile(session, callerNode);
			if (!string.IsNullOrEmpty(model))
				session.ModelId = model;

			int totalIn = 0, totalOut = 0;
			TurnResultModel? lastResult = null;

			for (var s = 0; s < stages.Count; s++)
			{
				// outputFormat guidance only applies to the last stage — earlier stages feed the
				// next stage's prompt, not the supervisor, so shaping them would be wasted guidance.
				var isLastStage = s == stages.Count - 1;
				var stagePrompt = stages[s].Replace("{{item}}", item);
				stagePrompt = isLastStage ? ApplyOutputFormat(stagePrompt, outputFormat) : stagePrompt;
				lastResult = await _turnService.Value.RunTurnAsync(session, stagePrompt, TurnSource.Orchestrated);
				totalIn  += lastResult.InputTokens;
				totalOut += lastResult.OutputTokens;

				if (budgetKey != null)
					_budgets[budgetKey].Accrue(lastResult.InputTokens + lastResult.OutputTokens);

				if (lastResult.IsError) break;
			}

			return new PipelineResult(item, lastResult?.AssistantText ?? string.Empty, totalIn, totalOut);
		}
		finally
		{
			_workerSemaphore.Release();
		}
	}

	private JsonNode WorkflowPhase(JsonNode? args, SessionNodeModel callerNode)
	{
		var title = args?["title"]?.GetValue<string>()
		            ?? throw new McpToolException("'title' is required");
		_fileService.AppendMessage(callerNode.FileName, Constants.SessionFile.RoleSystem, $"[Phase: {title}]");
		return BuildTextContent($"Phase '{title}' marked.");
	}

	private JsonNode WorkflowLog(JsonNode? args, SessionNodeModel callerNode)
	{
		var message = args?["message"]?.GetValue<string>()
		              ?? throw new McpToolException("'message' is required");
		_fileService.AppendMessage(callerNode.FileName, Constants.SessionFile.RoleSystem, $"[Log: {message}]");
		return BuildTextContent("Logged.");
	}

	private JsonNode GetBudget(SessionNodeModel callerNode)
	{
		var entries = _budgets
			.Where(kv => kv.Key.StartsWith(callerNode.NodeId + "/"))
			.Select(kv =>
			{
				var id = kv.Key[(callerNode.NodeId.Length + 1)..];
				return $"[{id}] spent={kv.Value.SpentTokens} budget={kv.Value.BudgetTokens} remaining={kv.Value.RemainingTokens}";
			})
			.ToList();

		return BuildTextContent(entries.Count == 0
			? "No active orchestration budgets."
			: string.Join("\n", entries));
	}

	private async Task<JsonNode> NotifyUserAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var message = args?["message"]?.GetValue<string>()
		              ?? throw new McpToolException("'message' is required");

		// FR.21.19: a Worker Session's call is dropped before any channel.
		if (callerNode.OrchestrationDepth > 0)
			return BuildTextContent("Notification not sent — worker sessions cannot notify the user. Report to your supervisor instead.");

		// FR.23.7: the message is also pinned as an Ask. The capture never notifies a second time.
		var pinnedSuffix = CaptureNotifyUserMessage(message, callerNode);

		// FR.21.5: Telegram and the desktop toast are independent — a failure in one must not stop the other.
		var channels = await NotifyChannelsAsync(callerNode, message);

		return BuildTextContent((channels.Count > 0
			? $"Notification sent ({string.Join(", ", channels)})."
			: "Notification not sent — neither the Telegram Bridge nor desktop notifications are enabled or reachable (see application logs).")
			+ pinnedSuffix);
	}

	/// <summary>
	/// The one Telegram + desktop fan-out, shared by <c>notify_user</c> (FR.21.5) and <c>post_attention</c>
	/// (FR.23.9). Returns the channels that delivered; each is gated as today and a failure in one never
	/// stops the other.
	/// </summary>
	private async Task<List<string>> NotifyChannelsAsync(SessionNodeModel callerNode, string message)
	{
		var channels = new List<string>();
		try
		{
			if (await _telegramBridge.NotifyAsync(callerNode.NodeId, message))
				channels.Add(Constants.Attention.ChannelTelegram);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "User notification: Telegram channel failed for node {NodeId}", callerNode.NodeId);
		}

		try
		{
			var title = string.Format(Constants.Attention.NotifyToastTitleFormat,
				_labelService.GetLabel(callerNode.WorkingDirectory), callerNode.Name);
			if (_notifications.ShowUserNotification(callerNode.NodeId, title, message))
				channels.Add(Constants.Attention.ChannelDesktop);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "User notification: desktop channel failed for node {NodeId}", callerNode.NodeId);
		}

		return channels;
	}

	/// <summary>
	/// FR.23.7 — pins a <c>notify_user</c> message as an Ask (source NotifyUser; title = first non-empty
	/// line). Returns the suffix for the tool result; a failure to pin must not stop the notification.
	/// </summary>
	private string CaptureNotifyUserMessage(string message, SessionNodeModel callerNode)
	{
		try
		{
			var firstLine = message.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
			var title     = NormalizeTitle(firstLine);
			var request = new AttentionPostRequestModel
			{
				Kind   = AttentionKind.Ask,
				Title  = title.Length > 0 ? title : Constants.Attention.NotifyUserFallbackTitle,
				Body   = Cut(message, Constants.Attention.MaxBodyLength),
				Source = AttentionSource.NotifyUser,
			};

			var outcome = _attention.Post(callerNode, [request]).Single();
			return string.Format(
				outcome.IsCreated ? Constants.Attention.PinnedAsSuffixFormat : Constants.Attention.AlreadyPinnedAsSuffixFormat,
				outcome.Item.Id);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "notify_user: could not pin the message as an Attention Item for node {NodeId}", callerNode.NodeId);
			return string.Empty;
		}
	}

	// ── Attention tools (FR.23) ───────────────────────────────────────────────

	private async Task<JsonNode> PostAttentionAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		// FR.23.6: a Worker Session gets a normal text result and nothing is stored.
		if (callerNode.OrchestrationDepth > 0)
			return BuildTextContent(Constants.Attention.WorkerRefusalText);

		var requests = ParseAttentionItems(args, callerNode);
		var notify   = GetOptionalBool(args, "notify") ?? false;

		var outcomes = _attention.Post(callerNode, requests);

		var lines = outcomes.Select(o => string.Format(
			o.IsCreated ? Constants.Attention.PostedLineFormat : Constants.Attention.AlreadyPinnedLineFormat,
			o.Item.Id, AttentionFormat.KindName(o.Item.Kind))).ToList();

		// FR.23.4: at most one notification per call; only created items count, updates never notify.
		var created = outcomes.Where(o => o.IsCreated).Select(o => o.Item).ToList();
		if (created.Count > 0 && (notify || created.Any(i => i.Kind == AttentionKind.Critical)))
		{
			var channels = await NotifyChannelsAsync(callerNode, BuildNewItemsMessage(created));
			lines.Add(channels.Count > 0
				? string.Format(Constants.Attention.NotifiedLineFormat, string.Join(Constants.Attention.ChannelSeparator, channels))
				: Constants.Attention.NotNotifiedLine);
		}

		lines.Add(Constants.Attention.PostClosingLine);
		return BuildTextContent(string.Join("\n", lines));
	}

	/// <summary>FR.23.9 message: one created item is "A12 (critical): title" + blank line + body; several are listed.</summary>
	private static string BuildNewItemsMessage(IReadOnlyList<AttentionItemModel> created)
	{
		if (created.Count == 1)
		{
			var item = created[0];
			var head = string.Format(Constants.Attention.NotifySingleFormat, item.Id, AttentionFormat.KindName(item.Kind), item.Title);
			return item.Body.Length == 0 ? head : head + "\n\n" + item.Body;
		}

		var sb = new StringBuilder(string.Format(Constants.Attention.NotifyManyHeaderFormat, created.Count));
		foreach (var item in created)
			sb.Append('\n').Append(string.Format(
				Constants.Attention.NotifyManyLineFormat, item.Id, AttentionFormat.KindName(item.Kind), item.Title));
		return sb.ToString();
	}

	/// <summary>Validates and normalises the whole <c>items</c> array before anything is posted (FR.23.4).</summary>
	private List<AttentionPostRequestModel> ParseAttentionItems(JsonNode? args, SessionNodeModel callerNode)
	{
		if (args?["items"] is not JsonArray items || items.Count == 0)
			throw new McpToolException("'items' is required and must contain at least one item.");
		if (items.Count > Constants.Attention.MaxItemsPerCall)
			throw new McpToolException($"'items' may contain at most {Constants.Attention.MaxItemsPerCall} items (got {items.Count}).");

		var requests = new List<AttentionPostRequestModel>();
		for (var n = 0; n < items.Count; n++)
		{
			if (items[n] is not JsonObject item)
				throw new McpToolException($"items[{n}] must be an object.");

			var kindText = GetOptionalString(item, "kind");
			if (string.IsNullOrWhiteSpace(kindText))
				throw new McpToolException($"items[{n}]: 'kind' is required ({AllowedKinds()}).");
			if (!TryParseKind(kindText, out var kind))
				throw new McpToolException($"items[{n}]: invalid kind '{kindText}' - use {AllowedKinds()}.");

			var title = NormalizeTitle(GetOptionalString(item, "title"));
			if (title.Length == 0)
				throw new McpToolException($"items[{n}]: 'title' is required and must not be empty.");

			var options = ReadStringArray(item, "options")
				.Select(o => Cut(o.Trim(), Constants.Attention.MaxOptionLength))
				.Where(o => o.Length > 0)
				.Take(Constants.Attention.MaxOptions)
				.ToList();

			var recommendation = GetOptionalString(item, "recommendation")?.Trim();
			var key            = GetOptionalString(item, "key")?.Trim();

			requests.Add(new AttentionPostRequestModel
			{
				Kind           = kind,
				Title          = title,
				Body           = Cut(GetOptionalString(item, "body") ?? string.Empty, Constants.Attention.MaxBodyLength),
				Options        = options,
				Recommendation = string.IsNullOrEmpty(recommendation) ? null : Cut(recommendation, Constants.Attention.MaxRecommendationLength),
				Key            = string.IsNullOrEmpty(key) ? null : Cut(key, Constants.Attention.MaxKeyLength),
				Source         = AttentionSource.Tool,
				Refs           = ResolveRefs(item, n, callerNode),
			});
		}

		return requests;
	}

	/// <summary>
	/// Reads <c>refs</c> of one item and resolves each against the transcripts and the disk. The first one that cannot be resolved fails
	/// the whole call with a message that names it, so nothing is pinned and the model can fix the reference and retry.
	/// </summary>
	private List<AttentionRefModel> ResolveRefs(JsonObject item, int itemIndex, SessionNodeModel callerNode)
	{
		var node = item["refs"];
		if (node == null)
			return [];
		if (node is not JsonArray array)
			throw new McpToolException($"items[{itemIndex}]: 'refs' must be an array.");
		if (array.Count > Constants.Attention.MaxRefsPerItem)
			throw new McpToolException($"items[{itemIndex}]: 'refs' may contain at most {Constants.Attention.MaxRefsPerItem} references (got {array.Count}).");

		var resolved = new List<AttentionRefModel>(array.Count);
		for (var m = 0; m < array.Count; m++)
		{
			var where = $"items[{itemIndex}].refs[{m}]";
			if (array[m] is not JsonObject refNode)
				throw new McpToolException($"{where} must be an object.");

			var kind = GetOptionalString(refNode, "kind")?.Trim().ToLowerInvariant();
			if (kind is not (Constants.Attention.RefKindEntry or Constants.Attention.RefKindFile or Constants.Attention.RefKindUrl))
				throw new McpToolException($"{where}: 'kind' is required - use entry, file or url.");

			if (_refs == null)
				throw new McpToolException($"{where}: references are not available.");

			var request = new FocusRefRequest(
				kind,
				Session: GetOptionalString(refNode, "session"),
				Quote: GetOptionalString(refNode, "quote"),
				Occurrence: GetOptionalString(refNode, "occurrence")?.Trim().ToLowerInvariant(),
				Path: GetOptionalString(refNode, "path"),
				Url: GetOptionalString(refNode, "url"),
				Label: GetOptionalString(refNode, "label"));

			try
			{
				resolved.Add(_refs.Resolve(callerNode, request));
			}
			catch (FocusRefException ex)
			{
				throw new McpToolException($"{where} ({kind}): {ex.Message}{Constants.Attention.RefsClosingHint}");
			}
		}

		return resolved;
	}

	private static bool TryParseKind(string text, out AttentionKind kind)
	{
		var trimmed = text.Trim();
		kind = default;
		// Enum.TryParse also accepts numeric strings; only the kind names are valid here.
		return Enum.GetNames<AttentionKind>().Any(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase))
		       && Enum.TryParse(trimmed, ignoreCase: true, out kind);
	}

	private static string AllowedKinds() =>
		string.Join(", ", Enum.GetValues<AttentionKind>().Select(AttentionFormat.KindName));

	private static List<string> ReadStringArray(JsonNode? node, string name)
	{
		var array = node?[name];
		if (array == null)
			return [];
		if (array is not JsonArray values)
			throw new McpToolException($"'{name}' must be an array of strings.");

		var result = new List<string>();
		foreach (var value in values)
		{
			if (value is JsonValue v && v.TryGetValue<string>(out var text))
				result.Add(text);
			else
				throw new McpToolException($"'{name}' must be an array of strings.");
		}

		return result;
	}

	/// <summary>Title: newlines become spaces, trimmed, cut to the limit (FR.23.4).</summary>
	private static string NormalizeTitle(string? raw) =>
		Cut(string.Join(' ', (raw ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
			.Select(l => l.Trim()).Where(l => l.Length > 0)), Constants.Attention.MaxTitleLength);

	private static string Cut(string text, int maxLength) => text.Length > maxLength ? text[..maxLength] : text;

	private JsonNode ResolveAttention(JsonNode? args, SessionNodeModel callerNode)
	{
		if (callerNode.OrchestrationDepth > 0)
			return BuildTextContent(Constants.Attention.WorkerRefusalText);

		var id = GetOptionalString(args, "id")?.Trim();
		if (string.IsNullOrEmpty(id))
			throw new McpToolException("'id' is required");

		var statusText = GetOptionalString(args, "status")?.Trim().ToLowerInvariant();
		var status = statusText switch
		{
			Constants.Attention.ResolveStatusAnswered => AttentionStatus.Answered,
			Constants.Attention.ResolveStatusResolved => AttentionStatus.Resolved,
			_ => throw new McpToolException(
				$"Invalid status '{statusText}' - use {Constants.Attention.ResolveStatusAnswered} or {Constants.Attention.ResolveStatusResolved}."),
		};

		var result = _attention.ResolveBySession(callerNode, id, status, GetOptionalString(args, "note"));
		return BuildTextContent(result.Outcome switch
		{
			AttentionResolveOutcome.Unknown => throw new McpToolException(
				string.Format(Constants.Attention.UnknownItemFormat, id.ToUpperInvariant())),
			AttentionResolveOutcome.AlreadyClosed => string.Format(
				Constants.Attention.AlreadyClosedFormat, result.Item!.Id, AttentionFormat.StatusName(result.Item.Status)),
			_ => string.Format(
				status == AttentionStatus.Answered ? Constants.Attention.ResolvedAnsweredFormat : Constants.Attention.ResolvedFormat,
				result.Item!.Id),
		});
	}

	private JsonNode ListAttention(JsonNode? args, SessionNodeModel callerNode)
	{
		var statusText = GetOptionalString(args, "status")?.Trim().ToLowerInvariant() ?? Constants.Attention.ListStatusOpen;
		var all        = statusText switch
		{
			Constants.Attention.ListStatusOpen => false,
			Constants.Attention.ListStatusAll  => true,
			_ => throw new McpToolException(
				$"Invalid status '{statusText}' - use {Constants.Attention.ListStatusOpen} or {Constants.Attention.ListStatusAll}."),
		};

		// FR.23.6: a Worker Session has no Focus view.
		if (callerNode.OrchestrationDepth > 0)
			return BuildTextContent(Constants.Attention.NoItemsText);

		var items = _attention.GetItems(callerNode.NodeId)
			.Where(i => all || i.Status == AttentionStatus.Open)
			.ToList();
		if (items.Count == 0)
			return BuildTextContent(all ? Constants.Attention.NoItemsText : Constants.Attention.NoOpenItemsText);

		return BuildTextContent(string.Join("\n", items.Select(FormatAttentionListLine)));
	}

	/// <summary>FR.23.5 line: <c>A12 [critical, open] title</c>; closed items add their answer or note.</summary>
	private static string FormatAttentionListLine(AttentionItemModel item)
	{
		var line = $"{item.Id} [{AttentionFormat.KindName(item.Kind)}, {AttentionFormat.StatusName(item.Status)}] {item.Title}";
		return item.Status switch
		{
			AttentionStatus.Answered when !string.IsNullOrEmpty(item.Answer)         => $"{line} - answer: {item.Answer}",
			AttentionStatus.Resolved when !string.IsNullOrEmpty(item.ResolutionNote) => $"{line} - {item.ResolutionNote}",
			_ => line,
		};
	}

	// ── Session Tags (FR.24) ──────────────────────────────────────────────────

	/// <summary>
	/// <c>tag_session</c> (FR.24.4): add and remove tags, or list them when given nothing. Worker Sessions may
	/// tag too (FR.24.6). One result line per entry (adds first), then the final Tags line; one SYSTEM entry
	/// when something changed.
	/// </summary>
	private JsonNode TagSession(JsonNode? args, SessionNodeModel callerNode)
	{
		var add    = ReadStringArray(args, "add");
		var remove = ReadStringArray(args, "remove");
		if (add.Count > Constants.SessionTags.MaxEntriesPerArray || remove.Count > Constants.SessionTags.MaxEntriesPerArray)
			throw new McpToolException(
				$"'add' and 'remove' may each contain at most {Constants.SessionTags.MaxEntriesPerArray} entries.");

		var lines = new List<string>();
		IReadOnlyList<SessionTagModel> tags;

		if (add.All(string.IsNullOrWhiteSpace) && remove.All(string.IsNullOrWhiteSpace))
			tags = _tags.GetTags(callerNode.NodeId, callerNode.Name);
		else
		{
			var result = _tags.Edit(callerNode, add, remove);
			lines.AddRange(result.Outcomes.Select(FormatTagOutcome));
			tags = result.Tags;

			if (result.HasChanges)
				_fileService.AppendMessage(callerNode.FileName, Constants.SessionFile.RoleSystem, BuildTagsSystemEntry(result));
		}

		lines.Add(string.Format(
			Constants.SessionTags.TagsLineFormat,
			tags.Count == 0 ? Constants.SessionTags.TagsNone : string.Join(Constants.SessionTags.LabelSeparator, tags.Select(t => t.Label))));
		return BuildTextContent(string.Join("\n", lines));
	}

	private static string FormatTagOutcome(SessionTagEntryOutcomeModel o) => o.Kind switch
	{
		SessionTagEntryOutcomeKind.Tagged               => string.Format(Constants.SessionTags.TaggedLineFormat, o.Tag!.Label, o.Tag.Url),
		SessionTagEntryOutcomeKind.AlreadyTagged        => string.Format(Constants.SessionTags.AlreadyTaggedLineFormat, o.Tag!.Label),
		SessionTagEntryOutcomeKind.Removed              => string.Format(Constants.SessionTags.RemovedLineFormat, o.Tag!.Label),
		SessionTagEntryOutcomeKind.NotTaggedInvalid     => string.Format(Constants.SessionTags.NotTaggedInvalidLineFormat, o.Entry),
		SessionTagEntryOutcomeKind.NotTaggedUnknownTeam => string.Format(
			Constants.SessionTags.NotTaggedUnknownTeamLineFormat, o.Entry, o.UnknownTeam, o.UnknownIssueKey),
		SessionTagEntryOutcomeKind.NotRemovedNotTagged  => string.Format(Constants.SessionTags.NotRemovedNotTaggedLineFormat, o.Entry),
		SessionTagEntryOutcomeKind.NotRemovedFromName   => string.Format(Constants.SessionTags.NotRemovedFromNameLineFormat, o.Entry),
		_                                               => string.Format(Constants.SessionTags.NotRemovedInvalidLineFormat, o.Entry),
	};

	/// <summary>FR.24.4: <c>[Tags] added HSE-230, windows-app PR #215; removed windows-app</c>; only the applied parts, by label.</summary>
	private static string BuildTagsSystemEntry(SessionTagEditResultModel result)
	{
		var parts = new List<string>();
		if (result.AddedLabels.Count > 0)
			parts.Add(string.Format(Constants.SessionTags.SystemAddedFormat, string.Join(Constants.SessionTags.LabelSeparator, result.AddedLabels)));
		if (result.RemovedLabels.Count > 0)
			parts.Add(string.Format(Constants.SessionTags.SystemRemovedFormat, string.Join(Constants.SessionTags.LabelSeparator, result.RemovedLabels)));

		return Constants.SessionTags.SystemEntryPrefix + " " + string.Join(Constants.SessionTags.SystemPartSeparator, parts);
	}

	// ── Helpers: tier enforcement + output shaping ────────────────────────────

	/// <summary>Resolves the caller's effective model for tier comparison (FR.16.4).</summary>
	private string? GetCallerEffectiveModel(SessionNodeModel callerNode)
	{
		if (!string.IsNullOrEmpty(callerNode.ModelId))
			return callerNode.ModelId;

		var dirModel = _appSettings.Settings.Tree.FirstOrDefault(d =>
			string.Equals(d.Path, callerNode.WorkingDirectory, StringComparison.OrdinalIgnoreCase));

		return !string.IsNullOrEmpty(dirModel?.SelectedModelId)
			? dirModel.SelectedModelId
			: _appSettings.Settings.SelectedModelId;
	}

	/// <summary>
	/// Throws <see cref="McpToolException"/> when the caller's tier is lower than the requested model's tier.
	/// </summary>
	private void EnforceModelTier(SessionNodeModel callerNode, string requestedModel)
	{
		var callerModel = GetCallerEffectiveModel(callerNode);
		var callerTier  = ModelTierService.GetTier(callerModel);
		var targetTier  = ModelTierService.GetTier(requestedModel);

		if (callerTier < targetTier)
			throw new McpToolException(
				$"Tier violation: a {ModelTierService.TierName(callerTier)} session cannot spawn " +
				$"a {ModelTierService.TierName(targetTier)} worker. " +
				$"Caller model: '{callerModel ?? "default"}', requested: '{requestedModel}'.");
	}

	/// <summary>
	/// Appends optional markdown output-shaping guidance to a worker prompt. Advisory only — not
	/// validated or retried, since the consumer of a worker's result is another session, which
	/// reads markdown far better than JSON. A worker that needs to hand back a large or structured
	/// artifact is told to persist it to a file and return the path instead.
	/// </summary>
	private static string ApplyOutputFormat(string prompt, string? outputFormat)
	{
		if (string.IsNullOrWhiteSpace(outputFormat))
			return prompt;

		return prompt + "\n\n---\nFormat your final response as follows:\n" + outputFormat +
		       "\n\nIf the result is a large or structured artifact, write it to a file in your " +
		       "working directory and return its path instead of inlining it.";
	}

	/// <summary>Assembles orchestrate_parallel worker results into one markdown document.</summary>
	private static string BuildWorkerResultsMarkdown(WorkerResult[] results)
	{
		var sb = new StringBuilder();
		foreach (var r in results)
		{
			sb.AppendLine($"## {r.Name}");
			sb.AppendLine();
			sb.AppendLine(r.Result);
			sb.AppendLine();
			sb.AppendLine($"_tokens: in={r.InputTokens} out={r.OutputTokens}_");
			sb.AppendLine();
		}
		return sb.ToString().TrimEnd();
	}

	/// <summary>Assembles orchestrate_pipeline item results into one markdown document.</summary>
	private static string BuildPipelineResultsMarkdown(PipelineResult[] results)
	{
		var sb = new StringBuilder();
		foreach (var r in results)
		{
			sb.AppendLine($"## {r.Item}");
			sb.AppendLine();
			sb.AppendLine(r.Result);
			sb.AppendLine();
			sb.AppendLine($"_tokens: in={r.InputTokens} out={r.OutputTokens}_");
			sb.AppendLine();
		}
		return sb.ToString().TrimEnd();
	}

	// ── JSON-RPC wire helpers ─────────────────────────────────────────────────

	private static async Task SendJsonRpcResultAsync(HttpListenerContext ctx, JsonNode? id, JsonNode result)
	{
		var response = new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"]      = id?.DeepClone(),
			["result"]  = result,
		};
		await WriteJsonResponseAsync(ctx, response.ToJsonString(), 200);
	}

	private static async Task SendJsonRpcErrorAsync(HttpListenerContext ctx, JsonNode? id, int code, string message)
	{
		var response = new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"]      = id?.DeepClone(),
			["error"]   = new JsonObject { ["code"] = code, ["message"] = message },
		};
		await WriteJsonResponseAsync(ctx, response.ToJsonString(), 200);
	}

	private static async Task SendErrorAsync(HttpListenerContext ctx, int code, string message)
	{
		var response = new JsonObject
		{
			["jsonrpc"] = "2.0",
			["id"]      = JsonValue.Create<object?>(null),
			["error"]   = new JsonObject { ["code"] = code, ["message"] = message },
		};
		await WriteJsonResponseAsync(ctx, response.ToJsonString(), 400);
	}

	private static async Task WriteJsonResponseAsync(HttpListenerContext ctx, string json, int statusCode)
	{
		var bytes                 = Encoding.UTF8.GetBytes(json);
		ctx.Response.StatusCode   = statusCode;
		ctx.Response.ContentType  = "application/json";
		ctx.Response.ContentLength64 = bytes.Length;
		await ctx.Response.OutputStream.WriteAsync(bytes);
		ctx.Response.Close();
	}

	private static JsonNode BuildTextContent(string text) =>
		new JsonObject
		{
			["content"] = new JsonArray(
				new JsonObject { ["type"] = "text", ["text"] = text }),
		};

	private static JsonNode BuildToolDef(string name, string description, JsonNode schema) =>
		new JsonObject
		{
			["name"]        = name,
			["description"] = description,
			["inputSchema"] = schema,
		};

	// ── Tool input schemas ────────────────────────────────────────────────────

	private static JsonNode ScheduleWakeSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"when": {
					"type": "object",
					"description": "Timing. Exactly one of: inSeconds (number), at (ISO 8601 string), cron (string)",
					"properties": {
						"inSeconds": { "type": "number" },
						"at":        { "type": "string" },
						"cron":      { "type": "string" }
					}
				},
				"prompt": { "type": "string", "description": "Prompt to send when the schedule fires." },
				"note":   { "type": "string", "description": "Human-readable label for list_schedules." },
				"target": { "type": "string", "description": "Target session (full NodeId or a unique prefix of at least 6 characters). Omit for self-wake." }
			},
			"required": ["when"]
		}
		""")!;

	private static JsonNode ListSchedulesSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"all": { "type": "boolean", "description": "Return all app schedules (true) or only caller's (false)." }
			}
		}
		""")!;

	private static JsonNode CancelScheduleSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"scheduleId": { "type": "string" }
			},
			"required": ["scheduleId"]
		}
		""")!;

	private static JsonNode ListSessionsSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"scope":             { "type": "string", "enum": ["project", "all", "children", "subtree"], "description": "project (default): sessions under your Directory Node; all: every Directory Node; children: the direct workers of nodeId; subtree: every descendant of nodeId." },
				"nodeId":            { "type": "string", "description": "Root session for scope=children|subtree (default: you). A full NodeId or a unique prefix of at least 6 characters is accepted." },
				"query":             { "type": "string", "description": "Case-insensitive substring of the session name or of a group name on its path, or a prefix of its NodeId." },
				"running":           { "type": "boolean", "description": "true: only sessions with a turn in progress; false: only idle ones." },
				"activeWithinHours": { "type": "integer", "description": "Only sessions whose last activity is within the last N hours (N > 0)." },
				"includeWorkers":    { "type": "boolean", "description": "scope=project|all: list every worker session as a row instead of only counting it on its supervisor's row. Default false." },
				"tags":              { "type": "array", "items": { "type": "string" }, "description": "Only sessions tagged with ANY of these (a worker counts for its top-level supervisor): Linear or GitHub URL, HSE-230, owner/repo#215 or owner/repo. At most 30. Find tags with search_tags." },
				"limit":             { "type": "integer", "description": "Rows per page, 1-200. Default 30." },
				"offset":            { "type": "integer", "description": "Rows to skip. Default 0." }
			}
		}
		""")!;

	private static JsonNode SearchTagsSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"query":  { "type": "string", "description": "Words that must all occur, case-insensitively, in the tag's label or key (e.g. 'backend 303', 'HSE-254'). The URL is not searched. Blank or absent: every tag." },
				"scope":  { "type": "string", "enum": ["project", "all"], "description": "project (default): tags of sessions under your Directory Node; all: every Directory Node." },
				"limit":  { "type": "integer", "description": "Tags per page, 1-200. Default 30." },
				"offset": { "type": "integer", "description": "Tags to skip. Default 0." }
			}
		}
		""")!;

	private static JsonNode EmptyObjectSchema() => JsonNode.Parse("""{ "type": "object", "properties": {} }""")!;

	private static JsonNode SpawnSessionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"name":         { "type": "string", "description": "Display name for the new session." },
				"workingDir":   { "type": "string", "description": "Filesystem working directory. Mutually exclusive with parentNodeId." },
				"parentNodeId": { "type": "string", "description": "Inherit working dir from this node (full NodeId or a unique prefix of at least 6 characters). Mutually exclusive with workingDir." },
				"prompt":       { "type": "string", "description": "Optional first prompt to run on the new session." },
				"group":        { "type": "string", "description": "Optional group name to place the session under." },
				"model":        { "type": "string", "description": "Model ID for the new session (tier-enforced, FR.16)." },
				"outputFormat": { "type": "string", "description": "Optional markdown/free-text description of the desired response shape for the first prompt (e.g. a section outline). Advisory guidance appended to the prompt — not validated. For a large or structured artifact, tell the worker to write a file and return its path instead of inlining it (FR.15.7)." }
			},
			"required": ["name"]
		}
		""")!;

	private static JsonNode SendToSessionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"nodeId":       { "type": "string", "description": "Session to resume. A full NodeId or a unique prefix of at least 6 characters is accepted." },
				"prompt":       { "type": "string" },
				"mode":         { "type": "string", "enum": ["wait", "async"], "description": "wait=block for result, async=return immediately and post the result back to you via mailbox. Async results that arrive while you are busy are delivered together in one turn once you are idle." },
				"outputFormat": { "type": "string", "description": "Optional markdown/free-text description of the desired response shape. Advisory guidance appended to the prompt — not validated (FR.15.7)." }
			},
			"required": ["nodeId", "prompt"]
		}
		""")!;

	private static JsonNode ReadSessionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"nodeId": { "type": "string", "description": "Session to read. A full NodeId or a unique prefix of at least 6 characters is accepted." },
				"lastN":  { "type": "integer", "description": "Max entries to return from end of file. 0=all." }
			},
			"required": ["nodeId"]
		}
		""")!;

	private static JsonNode StopSessionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"nodeId": { "type": "string", "description": "Session whose running turn to cancel. A full NodeId or a unique prefix of at least 6 characters is accepted." }
			},
			"required": ["nodeId"]
		}
		""")!;

	private static JsonNode SetSessionModelSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"nodeId": { "type": "string", "description": "Session to update. A full NodeId or a unique prefix of at least 6 characters is accepted." },
				"model":  { "type": "string", "description": "Model ID to set (e.g. 'claude-sonnet-4-6'). Tier-enforced." }
			},
			"required": ["nodeId", "model"]
		}
		""")!;

	private static JsonNode OrchestrateParallelSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"tasks": {
					"type": "array",
					"description": "Array of {name, prompt} objects. Each becomes one worker session.",
					"items": {
						"type": "object",
						"properties": {
							"name":   { "type": "string" },
							"prompt": { "type": "string" }
						},
						"required": ["name", "prompt"]
					}
				},
				"model":        { "type": "string", "description": "Model override for all workers (tier-enforced)." },
				"group":        { "type": "string", "description": "Group name for worker sessions in the tree." },
				"outputFormat": { "type": "string", "description": "Optional markdown/free-text description of the desired response shape, applied to every worker. Advisory guidance appended to each worker's prompt — not validated. Results come back as one markdown document, one section per worker (FR.15.7)." },
				"budgetTokens": { "type": "integer", "description": "Max total input+output tokens for this orchestration." }
			},
			"required": ["tasks"]
		}
		""")!;

	private static JsonNode OrchestratePipelineSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"items": {
					"type": "array",
					"description": "Array of item strings. Each item gets its own session.",
					"items": { "type": "string" }
				},
				"stages": {
					"type": "array",
					"description": "Array of {prompt} objects. Stages run sequentially within each item session. Use {{item}} in the first stage prompt.",
					"items": {
						"type": "object",
						"properties": {
							"prompt": { "type": "string" }
						},
						"required": ["prompt"]
					}
				},
				"model":        { "type": "string", "description": "Model override for all item sessions (tier-enforced)." },
				"group":        { "type": "string", "description": "Group name for item sessions in the tree." },
				"outputFormat": { "type": "string", "description": "Optional markdown/free-text description of the desired response shape, applied to the LAST stage prompt only. Advisory guidance — not validated. Results come back as one markdown document, one section per item (FR.15.7)." },
				"budgetTokens": { "type": "integer", "description": "Max total tokens for this pipeline run." }
			},
			"required": ["items", "stages"]
		}
		""")!;

	private static JsonNode WorkflowPhaseSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"title": { "type": "string", "description": "Phase title written to the session file." }
			},
			"required": ["title"]
		}
		""")!;

	private static JsonNode NotifyUserSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"message": { "type": "string", "description": "Notification or question text to send to the user (Telegram and desktop)." }
			},
			"required": ["message"]
		}
		""")!;

	private static JsonNode PostAttentionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"items": {
					"type": "array",
					"minItems": 1,
					"maxItems": 30,
					"description": "Items to pin, 1-30. An open item with the same key (or, without a key, the same title) is updated instead of duplicated.",
					"items": {
						"type": "object",
						"properties": {
							"kind":           { "type": "string", "enum": ["critical", "ask", "todo", "alert", "decided"], "description": "critical: the user must know immediately (always notifies). ask: you need their answer, decision or approval. todo: something only they can do. alert: worth knowing, no reply needed. decided: a call you made that they may override." },
							"title":          { "type": "string", "description": "One line, up to 200 characters." },
							"body":           { "type": "string", "description": "Markdown, self-contained, up to 20,000 characters." },
							"options":        { "type": "array", "maxItems": 10, "items": { "type": "string" }, "description": "Answer options the user can pick (each up to 200 characters)." },
							"recommendation": { "type": "string", "description": "Your recommended answer, up to 1,000 characters." },
							"key":            { "type": "string", "description": "Stable dedupe key of your choosing, up to 100 characters." },
							"refs": {
								"type": "array",
								"maxItems": 10,
								"description": "References that point the user at the exact thing the item is about, instead of pasting bulky output or a long plan into the body. kind=entry: a quote of an entry of this session (or of another one, see session); kind=file: a file you wrote or read; kind=url: a page. A reference that cannot be resolved fails the whole call - fix it and post again.",
								"items": {
									"type": "object",
									"properties": {
										"kind":       { "type": "string", "enum": ["entry", "file", "url"] },
										"quote":      { "type": "string", "maxLength": 300, "description": "entry: text copied exactly from the entry (whitespace is normalised, nothing else); it must occur exactly once in the entry. Quote a distinctive part, not the whole entry." },
										"session":    { "type": "string", "description": "entry: short id or node id of the session the entry belongs to. Default: this session." },
										"occurrence": { "type": "string", "enum": ["last", "first"], "description": "entry: which entry to pick when several hold the quote - the newest (last, default) or the oldest (first)." },
										"path":       { "type": "string", "description": "file: absolute path, or relative to this session's working directory. The file must exist." },
										"url":        { "type": "string", "description": "url: an http or https URL." },
										"label":      { "type": "string", "maxLength": 100, "description": "Short text the user sees for the reference." }
									},
									"required": ["kind"]
								}
							}
						},
						"required": ["kind", "title"]
					}
				},
				"notify": { "type": "boolean", "description": "Also ping the user's Telegram and desktop. Default false; critical items always notify." }
			},
			"required": ["items"]
		}
		""")!;

	private static JsonNode ResolveAttentionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"id":     { "type": "string", "description": "Item id, e.g. A12 (case-insensitive)." },
				"status": { "type": "string", "enum": ["answered", "resolved"], "description": "answered: the user answered it in conversation. resolved: done, withdrawn or no longer applies." },
				"note":   { "type": "string", "description": "answered: the user's answer. resolved: why. Up to 2,000 characters." }
			},
			"required": ["id", "status"]
		}
		""")!;

	private static JsonNode ListAttentionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"status": { "type": "string", "enum": ["open", "all"], "description": "open (default) or all." }
			}
		}
		""")!;

	private static JsonNode TagSessionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"add":    { "type": "array", "maxItems": 30, "items": { "type": "string" }, "description": "Tags to add: a Linear issue URL or key (HSE-230), a GitHub pull request URL or owner/repo#215, a GitHub repository URL or owner/repo." },
				"remove": { "type": "array", "maxItems": 30, "items": { "type": "string" }, "description": "Tags to remove, in the same forms. Tags that come from the session name cannot be removed." }
			}
		}
		""")!;

	private static JsonNode WorkflowLogSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"message": { "type": "string", "description": "Progress message written to the session file." }
			},
			"required": ["message"]
		}
		""")!;

	// ── Port selection ────────────────────────────────────────────────────────

	private static int FindFreePort()
	{
		var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = ((IPEndPoint)listener.LocalEndpoint).Port;
		listener.Stop();
		return port;
	}

	// ── Nested orchestration data types ──────────────────────────────────────

	/// <summary>Tracks token usage against a budget ceiling for one orchestration run (FR.15.6).</summary>
	private sealed class BudgetEntry(int budgetTokens)
	{
		private int _spent;

		public int BudgetTokens    => budgetTokens;
		public int SpentTokens     => _spent;
		public int RemainingTokens => Math.Max(0, budgetTokens - _spent);
		public bool IsExhausted    => _spent >= budgetTokens;

		public void Accrue(int tokens) => Interlocked.Add(ref _spent, tokens);
	}

	private sealed record WorkerResult(string Name, string Result, int InputTokens, int OutputTokens);
	private sealed record PipelineResult(string Item, string Result, int InputTokens, int OutputTokens);
}

// ── Private exceptions ────────────────────────────────────────────────────────

file sealed class McpMethodNotFoundException(string method) : Exception(method);
file sealed class McpToolException(string message) : Exception(message);
