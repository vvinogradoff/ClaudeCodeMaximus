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
using Avalonia.Threading;
using ClaudeMaximus.Models;
using ClaudeMaximus.Models.Agent;
using ClaudeMaximus.ViewModels;
using Json.Schema;
using Serilog;

namespace ClaudeMaximus.Services;

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
	private readonly SessionTreeViewModel      _sessionTree;
	private readonly ISessionFileService       _fileService;
	private readonly IClaudeModelService       _modelService;

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
		SessionTreeViewModel sessionTree,
		ISessionFileService fileService,
		IClaudeModelService modelService)
	{
		_appSettings  = appSettings;
		_turnService  = turnService;
		_scheduler    = scheduler;
		_sessionTree  = sessionTree;
		_fileService  = fileService;
		_modelService = modelService;
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
		_cts?.Cancel();
		try { _listener?.Stop(); } catch { /* best effort */ }
		_listener = null;
		_log.Information("AgentMcpServer stopped");
	}

	public Task<string?> EnsureConfigFileAsync(string nodeId, string agentToken)
	{
		if (!_appSettings.Settings.AgentToolsEnabled || _port == 0)
			return Task.FromResult<string?>(null);

		var dir  = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			Constants.AppDataFolderName,
			Constants.Agent.McpConfigFolderName);
		Directory.CreateDirectory(dir);

		var path    = Path.Combine(dir, $"{nodeId}.json");
		var content = BuildMcpConfigJson(agentToken);

		// Re-write if missing or the port/token changed.
		if (!File.Exists(path) || File.ReadAllText(path) != content)
			File.WriteAllText(path, content, Encoding.UTF8);

		return Task.FromResult<string?>(path);
	}

	// ── Private helpers ───────────────────────────────────────────────────────

	private string BuildMcpConfigJson(string agentToken)
	{
		// "type":"http" is required by the claude CLI to recognise this as an HTTP MCP server;
		// without it the entry is silently ignored (mcp_servers:[]).
		// The "headers" object is forwarded on every request so the server can map the token to the calling node.
		return JsonSerializer.Serialize(new
		{
			mcpServers = new Dictionary<string, object>
			{
				[Constants.Agent.McpServerName] = new
				{
					type    = "http",
					url     = $"http://127.0.0.1:{_port}{Constants.Agent.McpEndpointPath}",
					headers = new Dictionary<string, string>
					{
						[Constants.Agent.TokenHeader] = agentToken,
					},
				},
			},
		}, new JsonSerializerOptions { WriteIndented = true });
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

			// Resolve caller node from token on the UI thread.
			SessionNodeModel? callerNode = null;
			await Dispatcher.UIThread.InvokeAsync(() =>
				callerNode = _sessionTree.FindModelByAgentToken(token));

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
				"List all sessions in the tree with their status.",
				EmptyObjectSchema()),
			BuildToolDef("spawn_session",
				"Create a new persistent session node and optionally run the first turn. Model is tier-enforced (FR.16).",
				SpawnSessionSchema()),
			BuildToolDef("send_to_session",
				"Resume an existing session and run a turn. Use mode=async to not block. Supports schema-enforced output.",
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
				"Barrier fan-out: spawns one session per task, runs all concurrently, returns all results when done (FR.15.8).",
				OrchestrateParallelSchema()),
			BuildToolDef("orchestrate_pipeline",
				"Item pipeline: one session per item through sequential stages, all items run concurrently (FR.15.8).",
				OrchestratePipelineSchema()),
			BuildToolDef("workflow_phase",
				"Write a [Phase: title] progress marker to the supervisor session file (FR.15.9).",
				WorkflowPhaseSchema()),
			BuildToolDef("workflow_log",
				"Write a [Log: message] progress entry to the supervisor session file (FR.15.9).",
				WorkflowLogSchema()),
			BuildToolDef("get_budget",
				"Return token usage and remaining budget for the caller's active orchestrations (FR.15.6).",
				EmptyObjectSchema()));

		return new JsonObject { ["tools"] = tools };
	}

	private async Task<JsonNode> HandleToolCallAsync(JsonNode? @params, SessionNodeModel callerNode)
	{
		var name      = @params?["name"]?.GetValue<string>()
		                ?? throw new McpToolException("Missing tool name");
		var arguments = @params?["arguments"];

		return name switch
		{
			"schedule_wake"        => await ScheduleWakeAsync(arguments, callerNode),
			"list_schedules"       => ListSchedules(arguments, callerNode),
			"cancel_schedule"      => CancelSchedule(arguments),
			"list_sessions"        => await ListSessionsAsync(),
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
			_                      => throw new McpMethodNotFoundException(name),
		};
	}

	// ── Scheduling tools ──────────────────────────────────────────────────────

	private async Task<JsonNode> ScheduleWakeAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var when   = args?["when"] ?? throw new McpToolException("'when' is required");
		var prompt = args?["prompt"]?.GetValue<string>() ?? string.Empty;
		var note   = args?["note"]?.GetValue<string>() ?? string.Empty;
		var target = args?["target"]?.GetValue<string>();

		// Self-wake: no target → use caller's own NodeId.
		var targetNodeId = target ?? callerNode.NodeId;

		// Validate target exists.
		SessionNodeModel? targetNode = null;
		await Dispatcher.UIThread.InvokeAsync(() =>
			targetNode = _sessionTree.FindModelByNodeId(targetNodeId));

		if (targetNode == null)
			throw new McpToolException($"Target session not found: {targetNodeId}");

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

	private async Task<JsonNode> ListSessionsAsync()
	{
		List<SessionSummaryModel> summaries = [];
		await Dispatcher.UIThread.InvokeAsync(() =>
			summaries = _sessionTree.BuildAgentSessionSummaries());

		var lines = summaries.Select(s =>
			$"[{s.NodeId}] {s.Name} | {s.DirectoryLabel} | running={s.IsRunning} resumable={s.IsResumable}" +
			(s.LastPrompt != null ? $" | last: {s.LastPrompt[..Math.Min(60, s.LastPrompt.Length)]}" : ""));

		return BuildTextContent(
			summaries.Count == 0
				? "No sessions in tree."
				: string.Join("\n", lines));
	}

	private async Task<JsonNode> SpawnSessionAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var name       = args?["name"]?.GetValue<string>()
		                 ?? throw new McpToolException("'name' is required");
		var prompt     = args?["prompt"]?.GetValue<string>() ?? string.Empty;
		var group      = args?["group"]?.GetValue<string>();
		var modelArg   = args?["model"]?.GetValue<string>();
		var schemaNode = args?["schema"];

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
			SessionNodeModel? parentModel = null;
			await Dispatcher.UIThread.InvokeAsync(() =>
				parentModel = _sessionTree.FindModelByNodeId(parentNodeIdArg));
			if (parentModel == null)
				throw new McpToolException($"Parent node not found: {parentNodeIdArg}");
			workingDir = parentModel.WorkingDirectory;
		}
		else
			workingDir = callerNode.WorkingDirectory;

		// Create the node on the UI thread (NodeId/AgentToken backfilled inside CreateSessionForAgent).
		SessionNodeModel? newModel = null;
		await Dispatcher.UIThread.InvokeAsync(() =>
			newModel = _sessionTree.CreateSessionForAgent(workingDir, name, group,
			                                              childDepth, callerNode.NodeId));

		if (newModel == null)
			throw new McpToolException($"Could not create session under working dir: {workingDir}");

		// Persist model on the new session (FR.16.3).
		if (!string.IsNullOrEmpty(modelArg))
		{
			newModel.ModelId = modelArg;
			_appSettings.Save();
		}

		// Run first turn if a prompt was supplied.
		TurnResultModel? result = null;
		if (!string.IsNullOrEmpty(prompt))
		{
			result = await _turnService.Value.RunTurnAsync(newModel, prompt, TurnSource.Orchestrated);
			if (schemaNode != null && result != null)
				result = await ValidateAndRetryAsync(newModel, result, schemaNode.ToJsonString());
		}

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
		var schemaNode   = args?["schema"];

		SessionNodeModel? targetModel = null;
		await Dispatcher.UIThread.InvokeAsync(() =>
			targetModel = _sessionTree.FindModelByNodeId(targetNodeId));

		if (targetModel == null)
			throw new McpToolException($"Session not found: {targetNodeId}");

		if (mode == "async")
		{
			var callerSnapshot = callerNode;
			var targetSnapshot = targetModel;
			var schemaStr      = schemaNode?.ToJsonString();
			_ = Task.Run(async () =>
			{
				try
				{
					var result = await _turnService.Value.RunTurnAsync(
						targetSnapshot, prompt, TurnSource.Orchestrated);
					if (schemaStr != null)
						result = await ValidateAndRetryAsync(targetSnapshot, result, schemaStr);
					var workerName = targetSnapshot.Name;
					var mailboxMsg = $"{Constants.Agent.WorkerFinishedPrefix} {workerName}\n{result.AssistantText}";
					var mailbox    = new ScheduleModel
					{
						ScheduleId   = Guid.NewGuid().ToString("N")[..8],
						TargetNodeId = callerSnapshot.NodeId,
						Prompt       = mailboxMsg,
						Note         = $"Worker '{workerName}' result",
						Kind         = ScheduleKind.Delay,
						FireAtUtc    = DateTimeOffset.UtcNow,
					};
					_scheduler.Value.AddSchedule(mailbox);
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
			var result = await _turnService.Value.RunTurnAsync(targetModel, prompt, TurnSource.Orchestrated);
			if (schemaNode != null)
				result = await ValidateAndRetryAsync(targetModel, result, schemaNode.ToJsonString());
			return BuildTextContent(result.IsError
				? $"Error: {result.ErrorMessage}"
				: result.AssistantText);
		}
	}

	private async Task<JsonNode> ReadSessionAsync(JsonNode? args)
	{
		var nodeId = args?["nodeId"]?.GetValue<string>()
		             ?? throw new McpToolException("'nodeId' is required");
		var lastN  = args?["lastN"]?.GetValue<int>() ?? 20;

		SessionNodeModel? model = null;
		await Dispatcher.UIThread.InvokeAsync(() =>
			model = _sessionTree.FindModelByNodeId(nodeId));

		if (model == null)
			throw new McpToolException($"Session not found: {nodeId}");

		var entries = _fileService.ReadEntries(model.FileName);
		var relevant = entries
			.Where(e => e.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant)
			.TakeLast(lastN > 0 ? lastN : int.MaxValue)
			.ToList();

		if (relevant.Count == 0)
			return BuildTextContent("Session has no conversation entries.");

		var sb = new StringBuilder();
		foreach (var e in relevant)
		{
			sb.AppendLine($"[{e.Timestamp:u}] {e.Role}");
			sb.AppendLine(e.Content);
			sb.AppendLine();
		}
		return BuildTextContent(sb.ToString().TrimEnd());
	}

	private JsonNode StopSession(JsonNode? args)
	{
		var nodeId  = args?["nodeId"]?.GetValue<string>()
		              ?? throw new McpToolException("'nodeId' is required");
		var stopped = _turnService.Value.CancelTurn(nodeId);
		return BuildTextContent(stopped ? $"Cancelled turn for {nodeId}." : $"No active turn for {nodeId}.");
	}

	// ── New orchestration tools (FR.15.6–FR.15.10, FR.16) ───────────────────

	private async Task<JsonNode> SetSessionModelAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var targetNodeId = args?["nodeId"]?.GetValue<string>()
		                   ?? throw new McpToolException("'nodeId' is required");
		var model        = args?["model"]?.GetValue<string>()
		                   ?? throw new McpToolException("'model' is required");

		EnforceModelTier(callerNode, model);

		SessionNodeModel? target = null;
		await Dispatcher.UIThread.InvokeAsync(() =>
			target = _sessionTree.FindModelByNodeId(targetNodeId));

		if (target == null)
			throw new McpToolException($"Session not found: {targetNodeId}");

		target.ModelId = model;
		_appSettings.Save();
		return BuildTextContent($"Session '{target.Name}' ({targetNodeId}) model set to '{model}'. Effective from next turn.");
	}

	private async Task<JsonNode> OrchestrateParallelAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var tasksNode    = args?["tasks"]?.AsArray()
		                   ?? throw new McpToolException("'tasks' is required");
		var modelArg     = args?["model"]?.GetValue<string>();
		var group        = args?["group"]?.GetValue<string>() ?? "Orchestration";
		var schemaStr    = args?["schema"]?.ToJsonString();
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
			schemaStr, budgetTokens > 0 ? $"{callerNode.NodeId}/{orchestrationId}" : null)).ToArray();

		var results = await Task.WhenAll(taskRuns);

		// Clean up budget entry.
		_budgets.TryRemove($"{callerNode.NodeId}/{orchestrationId}", out _);

		return BuildTextContent(JsonSerializer.Serialize(results,
			new JsonSerializerOptions { WriteIndented = true }));
	}

	private async Task<JsonNode> OrchestratePipelineAsync(JsonNode? args, SessionNodeModel callerNode)
	{
		var itemsNode    = args?["items"]?.AsArray()
		                   ?? throw new McpToolException("'items' is required");
		var stagesNode   = args?["stages"]?.AsArray()
		                   ?? throw new McpToolException("'stages' is required");
		var modelArg     = args?["model"]?.GetValue<string>();
		var group        = args?["group"]?.GetValue<string>() ?? "Pipeline";
		var schemaStr    = args?["schema"]?.ToJsonString();
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
				schemaStr, budgetTokens > 0 ? $"{callerNode.NodeId}/{orchestrationId}" : null);
		}).ToArray();

		var results = await Task.WhenAll(itemRuns);
		_budgets.TryRemove($"{callerNode.NodeId}/{orchestrationId}", out _);

		return BuildTextContent(JsonSerializer.Serialize(results,
			new JsonSerializerOptions { WriteIndented = true }));
	}

	/// <summary>Runs a single parallel worker task: acquire slot → spawn session → run turn → validate → release.</summary>
	private async Task<WorkerResult> RunWorkerAsync(
		string name, string prompt, int index, SessionNodeModel callerNode,
		string? model, string group, int depth, string? schemaStr, string? budgetKey)
	{
		await _workerSemaphore.WaitAsync();
		try
		{
			if (budgetKey != null && _budgets.TryGetValue(budgetKey, out var budget) && budget.IsExhausted)
				return new WorkerResult(name, "[budget-exhausted]", 0, 0);

			SessionNodeModel? session = null;
			await Dispatcher.UIThread.InvokeAsync(() =>
				session = _sessionTree.CreateSessionForAgent(
					callerNode.WorkingDirectory, name, group, depth, callerNode.NodeId));

			if (session == null)
				return new WorkerResult(name, "[error: could not create session]", 0, 0);

			if (!string.IsNullOrEmpty(model))
				session.ModelId = model;

			var result = await _turnService.Value.RunTurnAsync(session, prompt, TurnSource.Orchestrated);
			if (schemaStr != null)
				result = await ValidateAndRetryAsync(session, result, schemaStr);

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
		string? model, string group, int depth, string? schemaStr, string? budgetKey)
	{
		await _workerSemaphore.WaitAsync();
		try
		{
			if (budgetKey != null && _budgets.TryGetValue(budgetKey, out var budget) && budget.IsExhausted)
				return new PipelineResult(item, "[budget-exhausted]", 0, 0);

			SessionNodeModel? session = null;
			await Dispatcher.UIThread.InvokeAsync(() =>
				session = _sessionTree.CreateSessionForAgent(
					callerNode.WorkingDirectory, $"pipeline-{index + 1}", group, depth, callerNode.NodeId));

			if (session == null)
				return new PipelineResult(item, "[error: could not create session]", 0, 0);

			if (!string.IsNullOrEmpty(model))
				session.ModelId = model;

			int totalIn = 0, totalOut = 0;
			TurnResultModel? lastResult = null;

			for (var s = 0; s < stages.Count; s++)
			{
				var stagePrompt = stages[s].Replace("{{item}}", item);
				lastResult = await _turnService.Value.RunTurnAsync(session, stagePrompt, TurnSource.Orchestrated);
				totalIn  += lastResult.InputTokens;
				totalOut += lastResult.OutputTokens;

				if (budgetKey != null)
					_budgets[budgetKey].Accrue(lastResult.InputTokens + lastResult.OutputTokens);

				if (lastResult.IsError) break;
			}

			// Schema validation on last stage output only.
			if (schemaStr != null && lastResult != null)
				lastResult = await ValidateAndRetryAsync(session, lastResult, schemaStr);

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

	// ── Helpers: tier enforcement + schema validation ─────────────────────────

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
	/// Validates <paramref name="result"/> against the JSON Schema string.
	/// On failure re-prompts up to <see cref="Constants.Agent.MaxSchemaRetries"/> times (FR.15.7).
	/// </summary>
	private async Task<TurnResultModel> ValidateAndRetryAsync(
		SessionNodeModel session, TurnResultModel result, string schemaStr)
	{
		JsonSchema? schema = null;
		try { schema = JsonSchema.FromText(schemaStr); }
		catch (Exception ex)
		{
			_log.Warning(ex, "Schema parse error — skipping validation");
			return result;
		}

		for (var attempt = 0; attempt < Constants.Agent.MaxSchemaRetries; attempt++)
		{
			if (string.IsNullOrWhiteSpace(result.AssistantText))
				break;

			bool isValid = false;
			try
			{
				using var doc = JsonDocument.Parse(result.AssistantText.Trim());
				var evalResult = schema.Evaluate(doc.RootElement);
				isValid = evalResult.IsValid;
			}
			catch { /* not JSON — fall through to re-prompt */ }

			if (isValid)
				return result;

			if (attempt == Constants.Agent.MaxSchemaRetries - 1)
				break;

			// Re-prompt with the schema constraint.
			var retryPrompt = $"Your previous response did not match the required JSON schema. " +
			                  $"Please respond with ONLY valid JSON matching this schema:\n{schemaStr}\n\n" +
			                  $"Do not include any text outside the JSON object.";
			result = await _turnService.Value.RunTurnAsync(session, retryPrompt, TurnSource.Orchestrated);
		}

		// Return with prefix so the supervisor knows validation failed.
		if (!result.AssistantText.StartsWith("[schema-invalid]"))
			return result with { AssistantText = "[schema-invalid] " + result.AssistantText };
		return result;
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
				"target": { "type": "string", "description": "NodeId of target session. Omit for self-wake." }
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

	private static JsonNode EmptyObjectSchema() => JsonNode.Parse("""{ "type": "object", "properties": {} }""")!;

	private static JsonNode SpawnSessionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"name":         { "type": "string", "description": "Display name for the new session." },
				"workingDir":   { "type": "string", "description": "Filesystem working directory. Mutually exclusive with parentNodeId." },
				"parentNodeId": { "type": "string", "description": "Inherit working dir from this node. Mutually exclusive with workingDir." },
				"prompt":       { "type": "string", "description": "Optional first prompt to run on the new session." },
				"group":        { "type": "string", "description": "Optional group name to place the session under." },
				"model":        { "type": "string", "description": "Model ID for the new session (tier-enforced, FR.16)." },
				"schema":       { "type": "object", "description": "JSON Schema for structured output from the first prompt (FR.15.7)." }
			},
			"required": ["name"]
		}
		""")!;

	private static JsonNode SendToSessionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"nodeId":  { "type": "string", "description": "NodeId of the session to resume." },
				"prompt":  { "type": "string" },
				"mode":    { "type": "string", "enum": ["wait", "async"], "description": "wait=block for result, async=return immediately and post result via mailbox." },
				"schema":  { "type": "object", "description": "JSON Schema for structured output (FR.15.7)." }
			},
			"required": ["nodeId", "prompt"]
		}
		""")!;

	private static JsonNode ReadSessionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"nodeId": { "type": "string" },
				"lastN":  { "type": "integer", "description": "Max entries to return from end of file. 0=all." }
			},
			"required": ["nodeId"]
		}
		""")!;

	private static JsonNode StopSessionSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"nodeId": { "type": "string" }
			},
			"required": ["nodeId"]
		}
		""")!;

	private static JsonNode SetSessionModelSchema() => JsonNode.Parse("""
		{
			"type": "object",
			"properties": {
				"nodeId": { "type": "string", "description": "NodeId of the session to update." },
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
				"schema":       { "type": "object", "description": "JSON Schema for structured worker output (FR.15.7)." },
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
				"schema":       { "type": "object", "description": "JSON Schema applied to the LAST stage output." },
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
