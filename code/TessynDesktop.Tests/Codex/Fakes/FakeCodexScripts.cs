using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace TessynDesktop.Tests.Codex.Fakes;

/// <summary>
/// Canned app-server exchanges, modelled on the FR.33.20 spike transcripts of Codex 0.171.0 (<c>%TEMP%\codex-spike\tx\</c>): the handshake,
/// account and rate-limit reads, the model list, threads, and turn scripts for the happy path, tools, server requests, errors, an
/// interrupt (a hang), compaction and a crash. Ids, e-mails and tokens are made up.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FakeCodexScripts
{
	public const string PowerShell = "\"C:\\WINDOWS\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\"";

	public JsonObject InitializeResult(string codexHome) => new()
	{
		["userAgent"]      = "tessyn_desktop/0.171.0 (Windows 10.0.26200; x86_64) xterm-256color (tessyn_desktop; 1.0.0)",
		["codexHome"]      = codexHome,
		["platformFamily"] = "windows",
		["platformOs"]     = "windows",
	};

	public JsonObject SignedInAccount(string email = "u@example.com", string plan = "plus") => new()
	{
		["account"]            = new JsonObject { ["type"] = "chatgpt", ["email"] = email, ["planType"] = plan, ["workspaceName"] = null },
		["userId"]             = "user-fake",
		["requiresOpenaiAuth"] = true,
	};

	public JsonObject SignedOutAccount() => new()
	{
		["account"]            = null,
		["userId"]             = null,
		["requiresOpenaiAuth"] = true,
	};

	public JsonObject RateLimitSnapshot(int? primaryUsed = 0, int secondaryUsed = 19, string? reached = null) => new()
	{
		["limitId"]              = "codex",
		["limitName"]            = null,
		["normalModelSlug"]      = null,
		["primary"]              = primaryUsed is null ? null : new JsonObject { ["usedPercent"] = primaryUsed, ["windowDurationMins"] = 300, ["resetsAt"] = 1795350371 },
		["secondary"]            = new JsonObject { ["usedPercent"] = secondaryUsed, ["windowDurationMins"] = 10080, ["resetsAt"] = 1795609786 },
		["credits"]              = new JsonObject { ["hasCredits"] = false, ["unlimited"] = false, ["balance"] = "0" },
		["individualLimit"]      = null,
		["spendControlReached"]  = false,
		["planType"]             = "plus",
		["rateLimitReachedType"] = reached,
	};

	public JsonObject RateLimitsResult() => new()
	{
		["ordinaryUsageAllowed"]   = true,
		["rateLimits"]             = RateLimitSnapshot(),
		["rateLimitsByLimitId"]    = new JsonObject { ["codex"] = RateLimitSnapshot() },
		["rateLimitResetCredits"]  = new JsonObject { ["availableCount"] = 3, ["credits"] = new JsonArray() },
		["accountId"]              = "acct-fake",
		["rateLimitUpsell"]        = null,
	};

	public JsonObject Model(string id, string displayName, bool isDefault, string defaultEffort, params string[] efforts) => new()
	{
		["id"]                        = id,
		["model"]                     = id,
		["displayName"]               = displayName,
		["description"]               = displayName + " model",
		["hidden"]                    = false,
		["isDefault"]                 = isDefault,
		["supportedReasoningEfforts"] = new JsonArray(efforts.Select(e => (JsonNode)new JsonObject { ["reasoningEffort"] = e, ["description"] = e + " reasoning" }).ToArray()),
		["defaultReasoningEffort"]    = defaultEffort,
		["inputModalities"]           = new JsonArray("text", "image"),
		["upgrade"]                   = null,
		["serviceTiers"]              = new JsonArray(new JsonObject { ["id"] = "priority", ["name"] = "Fast", ["description"] = "2x speed, increased usage" }),
		["additionalSpeedTiers"]      = new JsonArray("fast"),
		["multiAgentVersion"]         = "v2",
		["supportsPersonality"]       = false,
	};

	/// <summary>The spike's list, first part: the default sol model, astra and luna (no ultra on luna).</summary>
	public List<JsonArray> DefaultModelPages() =>
	[
		new JsonArray(
			Model("gpt-x-sol", "GPT-X-Sol", true, "low", "low", "medium", "high", "xhigh", "max", "ultra"),
			Model("gpt-x-astra", "GPT-X-Astra", false, "low", "low", "medium", "high", "xhigh", "max", "ultra"),
			Model("gpt-x-luna", "GPT-X-Luna", false, "medium", "low", "medium")),
	];

	public JsonObject Thread(string id, string? cwd, string? name = null, string? path = null) => new()
	{
		["id"]            = id,
		["sessionId"]     = id,
		["preview"]       = "",
		["ephemeral"]     = false,
		["modelProvider"] = "openai",
		["createdAt"]     = 1795332600,
		["updatedAt"]     = 1795332600,
		["status"]        = new JsonObject { ["type"] = "idle" },
		["path"]          = path,
		["cwd"]           = cwd,
		["cliVersion"]    = "0.171.0",
		["source"]        = "vscode",
		["threadSource"]  = "tessyn",
		["originator"]    = "tessyn_desktop",
		["name"]          = name,
		["turns"]         = new JsonArray(),
	};

	public JsonObject Turn(string id, string status, JsonObject? error = null) => new()
	{
		["id"]          = id,
		["rootTurnId"]  = id,
		["items"]       = new JsonArray(),
		["itemsView"]   = "notLoaded",
		["status"]      = status,
		["error"]       = error,
		["startedAt"]   = null,
		["completedAt"] = null,
		["durationMs"]  = null,
	};

	public JsonObject TurnError(JsonNode? codexErrorInfo, string message) => new()
	{
		["message"]           = message,
		["codexErrorInfo"]    = codexErrorInfo,
		["additionalDetails"] = null,
		["misalignment"]      = null,
	};

	public FakeCodexStep TurnStarted(string threadId, string turnId) =>
		FakeCodexStep.Notify("turn/started", new JsonObject { ["threadId"] = threadId, ["turn"] = Turn(turnId, "inProgress") });

	public FakeCodexStep TurnCompleted(string threadId, string turnId, string status, JsonObject? error = null) =>
		FakeCodexStep.Notify("turn/completed", new JsonObject { ["threadId"] = threadId, ["turn"] = Turn(turnId, status, error) });

	public FakeCodexStep ItemStarted(string threadId, string turnId, JsonObject item) =>
		FakeCodexStep.Notify("item/started", new JsonObject { ["item"] = item, ["threadId"] = threadId, ["turnId"] = turnId, ["startedAtMs"] = 1795332644927 });

	public FakeCodexStep ItemCompleted(string threadId, string turnId, JsonObject item) =>
		FakeCodexStep.Notify("item/completed", new JsonObject { ["item"] = item, ["threadId"] = threadId, ["turnId"] = turnId, ["completedAtMs"] = 1795332645026, ["contentTokenCount"] = null });

	public FakeCodexStep Delta(string threadId, string turnId, string itemId, string delta) =>
		FakeCodexStep.Notify("item/agentMessage/delta", new JsonObject { ["threadId"] = threadId, ["turnId"] = turnId, ["itemId"] = itemId, ["delta"] = delta });

	public FakeCodexStep ReasoningDelta(string threadId, string turnId, string itemId, string delta) =>
		FakeCodexStep.Notify("item/reasoning/summaryTextDelta", new JsonObject { ["threadId"] = threadId, ["turnId"] = turnId, ["itemId"] = itemId, ["delta"] = delta, ["summaryIndex"] = 0 });

	public FakeCodexStep TokenUsage(string threadId, string turnId, int input, int output, int reasoning) =>
		FakeCodexStep.Notify("thread/tokenUsage/updated", new JsonObject
		{
			["threadId"]   = threadId,
			["turnId"]     = turnId,
			["tokenUsage"] = new JsonObject
			{
				["total"]              = Breakdown(input * 2, output * 2, reasoning * 2),
				["last"]               = Breakdown(input, output, reasoning),
				["modelContextWindow"] = 475000,
			},
		});

	public FakeCodexStep RateLimitsUpdated() =>
		FakeCodexStep.Notify("account/rateLimits/updated", new JsonObject { ["rateLimits"] = RateLimitSnapshot() });

	public FakeCodexStep Error(string threadId, string turnId, JsonNode? codexErrorInfo, string message, bool willRetry) =>
		FakeCodexStep.Notify("error", new JsonObject { ["error"] = TurnError(codexErrorInfo, message), ["willRetry"] = willRetry, ["threadId"] = threadId, ["turnId"] = turnId });

	public JsonObject AgentMessage(string id, string text, string? phase = "final_answer") => new()
	{
		["type"] = "agentMessage", ["moderation"] = null, ["id"] = id, ["text"] = text, ["phase"] = phase, ["memoryCitation"] = null, ["delivery"] = null, ["questions"] = null,
	};

	public JsonObject UserMessage(string id, string text) => new()
	{
		["type"] = "userMessage", ["id"] = id, ["clientId"] = null,
		["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text, ["text_elements"] = new JsonArray() }),
	};

	public JsonObject CommandExecution(string id, string innerCommand, string status, int? exitCode) => new()
	{
		["type"] = "commandExecution", ["id"] = id, ["pluginId"] = null, ["scriptPath"] = null,
		["command"] = $"{PowerShell} -Command '{innerCommand.Replace("'", "''")}'",
		["cwd"] = @"C:\Proj\Alpha", ["processId"] = "79727", ["source"] = "unifiedExecStartup", ["status"] = status,
		["commandActions"] = new JsonArray(new JsonObject { ["type"] = "unknown", ["command"] = innerCommand }),
		["aggregatedOutput"] = status == "inProgress" ? null : "ok\r\n", ["exitCode"] = exitCode, ["durationMs"] = status == "inProgress" ? null : 98,
	};

	public JsonObject FileChange(string id, string status, params string[] paths) => new()
	{
		["type"] = "fileChange", ["id"] = id, ["status"] = status,
		["changes"] = new JsonArray(paths.Select(p => (JsonNode)new JsonObject { ["path"] = p, ["kind"] = new JsonObject { ["type"] = "add" }, ["diff"] = "edited by codex\n" }).ToArray()),
	};

	public JsonObject McpToolCall(string id, string server, string tool, string status, string? error = null) => new()
	{
		["type"] = "mcpToolCall", ["id"] = id, ["server"] = server, ["environmentId"] = null, ["tool"] = tool, ["status"] = status,
		["arguments"] = new JsonObject(), ["appContext"] = null, ["mcpAppUi"] = null, ["pluginId"] = null, ["readOnlyHint"] = null,
		["result"] = status == "completed" ? new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "X-CMX-Token=TOKEN-AAA" }) } : null,
		["error"] = error is null ? null : new JsonObject { ["message"] = error },
		["durationMs"] = 2,
	};

	public JsonObject Reasoning(string id) => new() { ["type"] = "reasoning", ["id"] = id, ["summary"] = new JsonArray(), ["content"] = new JsonArray() };

	/// <summary>A plain turn: the user message, then each text as one streamed agentMessage, a usage update per message, then completed.</summary>
	public List<FakeCodexStep> TextTurn(string threadId, string turnId, params string[] messages)
	{
		var steps = new List<FakeCodexStep>
		{
			TurnStarted(threadId, turnId),
			Notify("account/updated", new JsonObject { ["authMode"] = "chatgpt", ["planType"] = "plus", ["reason"] = "workspaceRoutingReady" }),
			ItemStarted(threadId, turnId, UserMessage($"user-{turnId}", "prompt")),
			ItemCompleted(threadId, turnId, UserMessage($"user-{turnId}", "prompt")),
		};

		for (var i = 0; i < messages.Length; i++)
		{
			var id    = $"msg-{turnId}-{i}";
			var text  = messages[i];
			var half  = text.Length / 2;
			var phase = i == messages.Length - 1 ? "final_answer" : "commentary";
			steps.Add(ItemStarted(threadId, turnId, AgentMessage(id, "", phase)));
			steps.Add(Delta(threadId, turnId, id, text[..half]));
			steps.Add(Delta(threadId, turnId, id, text[half..]));
			steps.Add(ItemCompleted(threadId, turnId, AgentMessage(id, text, phase)));
			steps.Add(TokenUsage(threadId, turnId, 15600 + i, 40 + i, 10));
			steps.Add(RateLimitsUpdated());
		}

		steps.Add(TurnCompleted(threadId, turnId, "completed"));
		return steps;
	}

	/// <summary>Modelled on spike turn D1: commentary, an MCP tool, a command, a file change, a plan, reasoning, a failed command, the answer.</summary>
	public List<FakeCodexStep> ToolTurn(string threadId, string turnId)
	{
		var steps = new List<FakeCodexStep>
		{
			TurnStarted(threadId, turnId),
			ItemStarted(threadId, turnId, AgentMessage("msg-c", "", "commentary")),
			ItemCompleted(threadId, turnId, AgentMessage("msg-c", "I'll run the three checks in order.", "commentary")),
			TokenUsage(threadId, turnId, 20653, 184, 31),
			ItemStarted(threadId, turnId, McpToolCall("mcp-1", "cmx", "post_attention", "inProgress")),
			ItemCompleted(threadId, turnId, McpToolCall("mcp-1", "cmx", "post_attention", "completed")),
			ItemStarted(threadId, turnId, CommandExecution("cmd-1", "cmd /c echo spike-cmd-ok", "inProgress", null)),
			ItemCompleted(threadId, turnId, CommandExecution("cmd-1", "cmd /c echo spike-cmd-ok", "completed", 0)),
			ItemStarted(threadId, turnId, FileChange("fc-1", "inProgress", @"C:\Proj\Alpha\spike-edit.txt", @"C:\Proj\Alpha\b.txt")),
			ItemCompleted(threadId, turnId, FileChange("fc-1", "completed", @"C:\Proj\Alpha\spike-edit.txt", @"C:\Proj\Alpha\b.txt")),
			Notify("turn/plan/updated", new JsonObject
			{
				["threadId"] = threadId, ["turnId"] = turnId, ["explanation"] = null,
				["plan"] = new JsonArray(
					new JsonObject { ["step"] = "Call the tool", ["status"] = "completed" },
					new JsonObject { ["step"] = "Run the command", ["status"] = "inProgress" },
					new JsonObject { ["step"] = "Edit the file", ["status"] = "pending" }),
			}),
			ItemStarted(threadId, turnId, Reasoning("rs-1")),
			ReasoningDelta(threadId, turnId, "rs-1", "Checking the file"),
			ItemCompleted(threadId, turnId, Reasoning("rs-1")),
			ItemStarted(threadId, turnId, CommandExecution("cmd-2", "Get-Content missing.txt", "inProgress", null)),
			ItemCompleted(threadId, turnId, CommandExecution("cmd-2", "Get-Content missing.txt", "failed", 1)),
			ItemStarted(threadId, turnId, new JsonObject { ["type"] = "webSearch", ["id"] = "ws-1", ["query"] = "codex app-server", ["action"] = null }),
			ItemCompleted(threadId, turnId, new JsonObject { ["type"] = "webSearch", ["id"] = "ws-1", ["query"] = "codex app-server", ["action"] = null }),
			ItemStarted(threadId, turnId, new JsonObject { ["type"] = "imageView", ["id"] = "iv-1", ["path"] = @"C:\Proj\Alpha\shot.png" }),
			ItemCompleted(threadId, turnId, new JsonObject { ["type"] = "imageView", ["id"] = "iv-1", ["path"] = @"C:\Proj\Alpha\shot.png" }),
			ItemStarted(threadId, turnId, new JsonObject { ["type"] = "collabAgentToolCall", ["id"] = "cb-1", ["tool"] = "spawnAgent", ["status"] = "inProgress" }),
			ItemStarted(threadId, turnId, AgentMessage("msg-f", "", "final_answer")),
			Delta(threadId, turnId, "msg-f", "All three "),
			Delta(threadId, turnId, "msg-f", "checks passed."),
			ItemCompleted(threadId, turnId, AgentMessage("msg-f", "All three checks passed.", "final_answer")),
			TokenUsage(threadId, turnId, 21000, 100, 20),
			RateLimitsUpdated(),
			FakeCodexStep.Notify("turn/diff/updated", new JsonObject { ["threadId"] = threadId, ["turnId"] = turnId, ["diff"] = "diff --git a/spike-edit.txt b/spike-edit.txt\n" }),
			TurnCompleted(threadId, turnId, "completed"),
		};
		return steps;
	}

	/// <summary>An agentMessage that streams its deltas and then never completes (a long answer), for an interrupt.</summary>
	public List<FakeCodexStep> StreamingThenHang(string threadId, string turnId, params string[] deltas)
	{
		var steps = new List<FakeCodexStep>
		{
			TurnStarted(threadId, turnId),
			ItemStarted(threadId, turnId, AgentMessage("msg-long", "", null)),
		};
		steps.AddRange(deltas.Select(d => Delta(threadId, turnId, "msg-long", d)));
		steps.Add(FakeCodexStep.Hang());
		return steps;
	}

	/// <summary>Spike item 9's failure shape: <c>error</c> with <c>willRetry: false</c>, then <c>turn/completed</c> failed with the same error.</summary>
	public List<FakeCodexStep> FailedTurn(string threadId, string turnId, JsonNode? codexErrorInfo, string message) =>
	[
		TurnStarted(threadId, turnId),
		Error(threadId, turnId, codexErrorInfo?.DeepClone(), message, willRetry: false),
		TurnCompleted(threadId, turnId, "failed", TurnError(codexErrorInfo?.DeepClone(), message)),
	];

	public List<FakeCodexStep> RetryThenCompleted(string threadId, string turnId)
	{
		var steps = new List<FakeCodexStep>
		{
			TurnStarted(threadId, turnId),
			Error(threadId, turnId, "serverOverloaded", "Reconnecting... 1/5", willRetry: true),
		};
		steps.AddRange(TextTurn(threadId, turnId, "Recovered.").Skip(1));
		return steps;
	}

	/// <summary>The spike's <c>thread/compact/start</c> sequence: a turn of its own with one <c>contextCompaction</c> item.</summary>
	public List<FakeCodexStep> CompactionTurn(string threadId, string turnId) =>
	[
		Notify("thread/status/changed", new JsonObject { ["threadId"] = threadId, ["status"] = new JsonObject { ["type"] = "active", ["activeFlags"] = new JsonArray() } }),
		TurnStarted(threadId, turnId),
		ItemStarted(threadId, turnId, new JsonObject { ["type"] = "contextCompaction", ["id"] = "cc-1" }),
		TokenUsage(threadId, turnId, 9000, 400, 0),
		RateLimitsUpdated(),
		ItemCompleted(threadId, turnId, new JsonObject { ["type"] = "contextCompaction", ["id"] = "cc-1" }),
		Notify("thread/status/changed", new JsonObject { ["threadId"] = threadId, ["status"] = new JsonObject { ["type"] = "idle" } }),
		TurnCompleted(threadId, turnId, "completed"),
	];

	/// <summary>A turn that sends every server request of FR.33.8 before it completes.</summary>
	public List<FakeCodexStep> ServerRequestTurn(string threadId, string turnId) =>
	[
		TurnStarted(threadId, turnId),
		FakeCodexStep.Request("item/commandExecution/requestApproval", new JsonObject
		{
			["threadId"] = threadId, ["turnId"] = turnId, ["itemId"] = "cmd-9", ["startedAtMs"] = 1, ["command"] = "git push", ["cwd"] = @"C:\Proj\Alpha", ["reason"] = "network",
		}),
		FakeCodexStep.Request("item/fileChange/requestApproval", new JsonObject
		{
			["threadId"] = threadId, ["turnId"] = turnId, ["itemId"] = "fc-9", ["startedAtMs"] = 1, ["reason"] = "outside the workspace", ["grantRoot"] = @"C:\Other",
		}),
		FakeCodexStep.Request("item/permissions/requestApproval", new JsonObject
		{
			["threadId"] = threadId, ["turnId"] = turnId, ["itemId"] = "perm-9", ["startedAtMs"] = 1, ["cwd"] = @"C:\Proj\Alpha", ["reason"] = "needs network",
			["permissions"] = new JsonObject { ["network"] = new JsonObject { ["enabled"] = true } },
		}),
		FakeCodexStep.Request("mcpServer/elicitation/request", new JsonObject
		{
			["threadId"] = threadId, ["turnId"] = turnId, ["serverName"] = "registry-x", ["mode"] = "form", ["message"] = "Pick one",
			["requestedSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }, ["_meta"] = null,
		}),
		FakeCodexStep.Request("item/tool/requestUserInput", new JsonObject
		{
			["threadId"] = threadId, ["turnId"] = turnId, ["itemId"] = "q-9", ["questions"] = new JsonArray(), ["isBlocking"] = true,
		}),
		FakeCodexStep.Request("brand/new/request", new JsonObject { ["threadId"] = threadId }),
		.. TextTurn(threadId, turnId, "Done after the requests.").Skip(1),
	];

	public List<FakeCodexStep> CrashMidTurn(string threadId, string turnId, int exitCode, params string[] stderr) =>
	[
		TurnStarted(threadId, turnId),
		ItemStarted(threadId, turnId, AgentMessage("msg-crash", "", null)),
		Delta(threadId, turnId, "msg-crash", "Starting the"),
		FakeCodexStep.Crash(exitCode, stderr),
	];

	private FakeCodexStep Notify(string method, JsonObject parameters) => FakeCodexStep.Notify(method, parameters);

	private JsonObject Breakdown(int input, int output, int reasoning) => new()
	{
		["totalTokens"]           = input + output,
		["inputTokens"]           = input,
		["cachedInputTokens"]     = input * 8 / 10,
		["cacheWriteInputTokens"] = 0,
		["outputTokens"]          = output,
		["reasoningOutputTokens"] = reasoning,
	};
}
