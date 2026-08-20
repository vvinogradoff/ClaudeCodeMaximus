using System;

namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static class Constants
{
	public const string AppDataFolderName = "TessynDesktop";
	public const string SettingsFileName = "appsettings.json";
	public const string DefaultSessionsFolderName = "sessions";
	public const string DraftsFolderName = "drafts";
	public const string ProfilesFolderName = "profiles";
	public const string DraftFileExtension = ".draft";
	public const int DraftDebounceMilliseconds = 500;
	public const int AutocompleteDebounceMilliseconds = 150;
	public const string SessionFileExtension = ".txt";
	public const string SessionFileNameDateFormat = "yyyy-MM-dd-HHmm";
	public const int SessionFileNameRandomSuffixLength = 6;

	public static class SessionFile
	{
		public const string RoleUser = "USER";
		public const string RoleAssistant = "ASSISTANT";
		public const string RoleSystem = "SYSTEM";
		public const string RoleCompaction = "COMPACTION";
		public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";
	}

	public static class ContextRestore
	{
		public const string NoConversationFoundMarker = "No conversation found";
	}

	public static class Auth
	{
		/// <summary>Substring of the Claude CLI's 401 error text (FR.12.16).</summary>
		public const string InvalidCredentialsMarker = "Invalid authentication credentials";

		/// <summary>Suffix appended to a removed profile's config directory name (FR.12.18).</summary>
		public const string RemovedDirectorySuffix = "_rem";
	}

	public static class CodeIndex
	{
		public const int DebounceMilliseconds = 300;
		public const int MaxSuggestions = 15;
		public const string SymbolTrigger = "#";
		public const string FileTrigger = "##";
	}

	public static class Instructions
	{
		public const string Delimiter = "\n\n---\nSession preferences — apply these if relevant to our work here:";

		/// <summary>
		/// The previous delimiter wording, kept only so stripping (<see cref="ClaudeSessionImportService"/>)
		/// still recognizes instruction blocks appended to older JSONL sessions before the wording changed.
		/// </summary>
		public const string LegacyDelimiter = "\n\n---\n[Additional instructions — do not acknowledge these in your response]";

		public const string AutoCommitOn = "Once you have completed the request, commit all your changes to git with a concise commit message.";
		public const string AutoCommitOff = "Do not commit any changes to git.";
		public const string NewBranch = "Create a new git branch before committing your changes.";
		public const string AutoDocument = "After completing the request, update any relevant requirements documents and/or architecture documents in the project's /docs directory to reflect the changes you made. If any new domain terms were introduced or existing terms changed, also update /docs/glossary.md.";
		public const string Clear = "After completing this request, please summarize the key outcomes and decisions from this session in a brief closing statement.";

		/// <summary>
		/// FR.14.11 — Redirects Claude away from the CLI's built-in scheduling tools toward the
		/// host MCP-served equivalents, because the claude process is terminated between turns
		/// and any native-tool schedules would be silently lost. Only emitted in One-shot Mode
		/// (FR.19.9) — omitted entirely for a session in Streaming Session Mode.
		/// </summary>
		public const string NativeSchedulingRedirect =
			"This CLI is terminated between turns. Do NOT use `ScheduleWakeup`, `CronCreate`, `CronList`, or `CronDelete` — schedules set via those tools will not survive. Use the TessynDesktop MCP tools instead: `mcp__" + Agent.McpServerName + "__schedule_wake` (accepts `inSeconds`, `at` ISO time, or `cron`), `mcp__" + Agent.McpServerName + "__list_schedules`, `mcp__" + Agent.McpServerName + "__cancel_schedule`. These are persisted by the host application and re-arm across CLI restarts.";

		/// <summary>
		/// FR.19.10 — Native tool names passed to <c>--disallowed-tools</c> on One-shot Mode spawns
		/// when AgentToolsEnabled is true, hard-blocking the tools <see cref="NativeSchedulingRedirect"/>
		/// only advises against. Not applied to Streaming Session Mode spawns.
		/// </summary>
		public static readonly string[] NativeSchedulingToolNames = ["ScheduleWakeup", "CronCreate", "CronList", "CronDelete"];

		/// <summary>
		/// Redirects Claude away from the Claude Code <c>Workflow</c> tool, the <c>Agent</c> tool's
		/// multi-agent orchestration, and the <c>/deep-research</c> skill toward TessynDesktop session
		/// orchestration MCP tools. All three spin off background processes/subagents that are
		/// invisible to TessynDesktop and whose results are not tracked in the session tree.
		/// TessynDesktop MCP tools produce persistent, visible session nodes instead.
		/// </summary>
		public const string NoWorkflowTool =
			"Do NOT use the Claude Code `Workflow` tool. Do NOT use the `Agent` tool for multi-agent orchestration. Do NOT use the `/deep-research` skill. These spawn processes/subagents that are invisible to TessynDesktop and untracked in the session tree. " +
			"Instead, use the TessynDesktop MCP tools for all multi-agent, orchestration, and research fan-out work: " +
			"`mcp__" + Agent.McpServerName + "__spawn_session` to create child sessions (supports `model`, `outputFormat` args), " +
			"`mcp__" + Agent.McpServerName + "__send_to_session` to send prompts (use `wait` mode for sequential work, `async` for parallel fan-out; supports `outputFormat` arg for a markdown shape hint), " +
			"`mcp__" + Agent.McpServerName + "__read_session` to retrieve results, " +
			"`mcp__" + Agent.McpServerName + "__stop_session` to cancel a running turn, " +
			"`mcp__" + Agent.McpServerName + "__list_sessions` to inspect the session tree, " +
			"`mcp__" + Agent.McpServerName + "__orchestrate_parallel` for concurrent fan-out over a task array (returns all results together as one markdown document), " +
			"`mcp__" + Agent.McpServerName + "__orchestrate_pipeline` for pipelined fan-out where items flow through stages independently (no barrier; returns one markdown document), " +
			"`mcp__" + Agent.McpServerName + "__workflow_phase` to mark a named phase for progress tracking in the UI, " +
			"`mcp__" + Agent.McpServerName + "__workflow_log` to emit a progress message visible in the session tree, " +
			"`mcp__" + Agent.McpServerName + "__get_budget` to check remaining token budget for the current orchestration, and " +
			"`mcp__" + Agent.McpServerName + "__set_session_model` to switch the model of a child session mid-orchestration. " +
			"For detailed guidance on orchestrating multi-session workflows, or on how to respond if you were spawned as a worker by another session, use the `" + Agent.OrchestrationSkillName + "` skill.";

		// Mid-run correction prompts (sent when user toggles flags while Claude is thinking)
		public const string MidRunAutoCommitOn = "Additional instruction: Once you have completed the request, commit all your changes to git with a concise commit message.";
		public const string MidRunAutoCommitOff = "Correction: Ignore previous instructions about committing to git. Do not commit any changes.";
		public const string MidRunAutoCommitSilent = "Correction: Disregard any earlier commit-related instruction. There is no preference on whether you commit or not.";
		public const string MidRunNewBranchOn = "Additional instruction: Create a new git branch before committing your changes.";
		public const string MidRunNewBranchOff = "Correction: Ignore previous instructions about creating a new git branch. Do not create a new branch.";
		public const string MidRunAutoDocumentOn = "Additional instruction: After completing the request, update any relevant requirements documents and/or architecture documents in the project's /docs directory to reflect the changes you made. If any new domain terms were introduced or existing terms changed, also update /docs/glossary.md.";
		public const string MidRunAutoDocumentOff = "Correction: Ignore previous instructions about updating documentation. Do not update any documentation files.";
		public const string MidRunAutoCompactOn = "[Auto-compact has been enabled — the session will be compacted after this response completes.]";
		public const string MidRunAutoCompactOff = "[Auto-compact has been disabled — the session will not be compacted after this response.]";

		[Obsolete("Looks like the functionality was superceded by the CompactionPromptTemplate, which includes glossary injection. Use that instead.", true)]
		/// <summary>Simple compaction prompt for the Tessyn daemon path (no glossary injection).</summary>
		public const string CompactionPrompt = """
Please compact the conversation in this session.

WHAT TO PRESERVE:
- Decisions made during development and the reasoning behind them
- Architecture choices and implementation details that matter
- The attribution that specific instructions or knowledge came from the user
- ALL URLs (full or partial) — never drop URLs, they are important context for searching sessions later

WHAT TO REMOVE:
- Transient information: debugging steps, intermediate failed attempts, progress updates, unnecessary verbosity
- Meta-instructions from the user such as: commit/no-commit instructions, auto-document instructions, session compaction instructions, and any other process directives unrelated to the actual development work
- Redundant back-and-forth about minor corrections or small fixes

HOW TO RESTRUCTURE USER PROMPTS:
- Group user inputs by semantic topic. Do NOT preserve every individual user message as a separate entry.
- Use the timestamp of the FIRST user message in each semantic group as the entry timestamp.
- Merge related follow-up messages (minor corrections, clarifications, small feature additions on the same topic) into the first message of that group.
- Only start a new USER entry when the topic/intent meaningfully changes.
- Rephrase for brevity and clarity while keeping the user's voice and intent clear.

Output the compacted conversation in this EXACT format (no preamble, no wrapping text, start directly with the first entry):

[2026-01-01T00:00:00Z] USER
<compacted user prompt — may merge multiple related user messages>

[2026-01-01T00:00:00Z] ASSISTANT
<compacted assistant response — may merge multiple related responses>

Use the original timestamps from the conversation. Each entry starts with a [timestamp] ROLE header line, followed by the content, followed by a blank line. Do NOT include any text before the first [timestamp] header or after the last entry.
""";

		public const string CompactionPromptTemplate = """
Please compact the conversation in this session.

WHAT TO PRESERVE:
- Decisions made during development and the reasoning behind them
- Architecture choices and implementation details that matter
- The attribution that specific instructions or knowledge came from the user
- ALL URLs (full or partial) — never drop URLs, they are important context for searching sessions later
- ALL secrets, API keys, tokens, credentials, and connection strings — these MUST always be preserved verbatim, no exceptions
- File names and paths: keep them whenever the surrounding context is preserved. If a segment describes work done on a specific file, the file name/path MUST stay. Only drop file names when removing the entire segment that references them

WHAT TO REMOVE:
- Transient information: debugging steps, intermediate failed attempts, progress updates, unnecessary verbosity
- Meta-instructions from the user such as: commit/no-commit instructions, auto-document instructions, session compaction instructions, and any other process directives unrelated to the actual development work
- Redundant back-and-forth about minor corrections or small fixes

TERMINOLOGY NORMALIZATION:
- The project has a glossary of domain terms (included below if available).
- When compacting, ALWAYS use the glossary term instead of informal/ad-hoc descriptions the user may have used. For example, if the glossary defines "Input Area" and the user said "the text box where I type my prompt", the compacted text must use "Input Area".
- If the conversation introduced new domain terms or concepts not in the glossary, note them with [NEW TERM] so they can be added to the glossary later. Also update /docs/glossary.md to include the new terms.
{0}
HOW TO RESTRUCTURE USER PROMPTS:
- Group user inputs by semantic topic. Do NOT preserve every individual user message as a separate entry.
- Use the timestamp of the FIRST user message in each semantic group as the entry timestamp.
- Merge related follow-up messages (minor corrections, clarifications, small feature additions on the same topic) into the first message of that group.
- Only start a new USER entry when the topic/intent meaningfully changes.
- Rephrase for brevity and clarity while keeping the user's voice and intent clear.

Output the compacted conversation in this EXACT format (no preamble, no wrapping text, start directly with the first entry):

[2026-01-01T00:00:00Z] USER
<compacted user prompt — may merge multiple related user messages>

[2026-01-01T00:00:00Z] ASSISTANT
<compacted assistant response — may merge multiple related responses>

Use the original timestamps from the conversation. Each entry starts with a [timestamp] ROLE header line, followed by the content, followed by a blank line. Do NOT include any text before the first [timestamp] header or after the last entry.

SESSION TRANSCRIPT TO COMPACT:
---
{1}
---
""";
	}

	public static class ClaudeAssist
	{
		public const int TitleBatchSize = 20;
		public const int TimeoutMs = 60000;
		public const string PreferredModel = "haiku";

		/// <summary>
		/// Full curated-catalog model ID (FR.18.7) for the preferred Haiku model. Single source of
		/// truth for both the catalog entry (ClaudeModelService) and the ANTHROPIC_SMALL_FAST_MODEL
		/// env var (FR.12.20) — both need the versioned ID, not just the "haiku" alias.
		/// </summary>
		public const string PreferredModelId = "claude-haiku-4-5-20251001";
	}

	public static class KeyBindings
	{
		public const string ImportSessions = "ImportSessions";
		public const string AddDirectory = "AddDirectory";
		public const string OpenSettings = "OpenSettings";
		public const string CloseDialog = "CloseDialog";
		public const string Send = "Send";
	}

	/// <summary>Constants for the agent MCP server and orchestration guardrails (FR.14, FR.15).</summary>
	public static class Agent
	{
		public const string McpServerName        = "cmx";
		public const string McpConfigFolderName  = "mcp";
		public const string McpConfigFileName    = "mcp-config.json";
		public const string TokenHeader          = "X-CMX-Token";
		public const string McpEndpointPath      = "/mcp";
		public const string TdcpEndpointPath     = "/tdcp";

		/// <summary>Skill name and on-disk layout for the orchestration skill (FR.15.11).
		/// Provisioned under each profile's config dir as skills/{OrchestrationSkillName}/SKILL.md.</summary>
		public const string OrchestrationSkillName       = "cmx-orchestration";
		public const string SkillsFolderName             = "skills";
		public const string SkillFileName                = "SKILL.md";

		/// <summary>Maximum supervisor-worker nesting depth (FR.15.5).</summary>
		public const int MaxOrchestrationDepth = 5;

		/// <summary>Maximum simultaneous worker turns across all supervisor sessions (FR.15.5).</summary>
		public const int MaxConcurrentWorkers = 10;

		/// <summary>Maximum cron fires per schedule before auto-cancellation (FR.15.5). 0 = unlimited.</summary>
		public const int MaxTurnsPerLoop = 100;

		/// <summary>
		/// Tier assigned to sessions whose effective model is not configured (FR.16.4).
		/// Defaults to 2 (Sonnet) so unconfigured sessions can still spawn Haiku/Local workers.
		/// </summary>
		public const int DefaultModelTier = 2;

		/// <summary>MCP JSON-RPC protocol version string. Must match what the claude CLI sends in its initialize request.</summary>
		public const string ProtocolVersion = "2025-11-25";

		/// <summary>Prefix prepended to scheduled turn prompts as a SYSTEM message.</summary>
		public const string ScheduledTurnSystemPrefix = "[Scheduled]";

		/// <summary>Note recorded on a schedule created via the Send button's right-click "delay send" popup.</summary>
		public const string DelayedSendNote = "Delayed send";

		/// <summary>Prefix used in async mailbox messages posted to the supervisor.</summary>
		public const string WorkerFinishedPrefix = "[Worker finished]";

		/// <summary>Timeout in ms for a scheduled turn; after this the turn is cancelled.</summary>
		public const int ScheduledTurnTimeoutMs = 3_600_000; // 1 hour

		/// <summary>
		/// Body of the <see cref="OrchestrationSkillName"/> skill, provisioned by
		/// SkillProvisioningService into every profile's skills folder (FR.15.11). Loads on demand —
		/// only the frontmatter `description` below is always visible to a session; the body is
		/// pulled in only when the session decides it needs orchestration guidance.
		/// </summary>
		public const string OrchestrationSkillMarkdown = """
			---
			name: cmx-orchestration
			description: How to orchestrate multi-session workflows in TessynDesktop (fan-out, pipelines, budgets, progress markers) and how to behave when you were spawned as a worker by another session. Use when spinning off sub-sessions / complex workflows, or when you were spawned by another session and need to know how to respond.
			---

			# TessynDesktop session orchestration

			TessynDesktop hosts every Claude Code session as a tree node with its own persistent
			transcript. The `mcp__cmx__*` tools let one session (a supervisor) spin off other sessions
			(workers) and collect their results. Do NOT use the Claude Code `Workflow` tool, the `Agent`
			tool, or `/deep-research` for this — they spawn processes invisible to TessynDesktop and
			untracked in the session tree. Use the tools below instead.

			## As a supervisor

			Choose the right primitive:
			- `spawn_session` — create one new child session and optionally run its first turn.
			- `send_to_session` — resume an existing session node. Use `mode=wait` for sequential work
			  you need the result of before continuing; `mode=async` to fire-and-continue — the result
			  posts back to you automatically when the worker finishes.
			- `orchestrate_parallel` — barrier fan-out: give it a `tasks` array of `{name, prompt}`, it
			  spawns one session per task, runs them all concurrently, and returns everything as a
			  single markdown document (one `## name` section per task) once ALL tasks finish.
			- `orchestrate_pipeline` — non-blocking item fan-out: give it an `items` array and a
			  `stages` array of `{prompt}` (use `{{item}}` in stage prompts), each item runs through all
			  stages sequentially in its own session, all items run concurrently. Returns one markdown
			  document (one `## item` section) when all items finish.

			Shaping worker output: pass `outputFormat` (a markdown/free-text description of the shape
			you want, e.g. a section outline) — it's advisory guidance appended to the worker's prompt,
			not a validated schema. Don't ask for JSON back-and-forth between sessions — markdown is
			what an LLM reads and writes best. If a worker produces something genuinely large or
			structured (data files, generated code, a big table), tell it to write a file in the working
			directory and return the path instead of inlining the content.

			Other tools: `list_sessions` to inspect the tree before deciding new-vs-resume;
			`read_session` to pull a worker's transcript directly; `stop_session` to cancel a running
			turn; `set_session_model` to change a child's model mid-orchestration (tier-enforced);
			`workflow_phase`/`workflow_log` to mark progress visible in the session tree UI;
			`budgetTokens` on the fan-out tools plus `get_budget` to track token spend. Depth
			(`MaxOrchestrationDepth`) and concurrency (`MaxConcurrentWorkers`) limits are enforced —
			expect an error if you nest or fan out too aggressively.

			## As a worker

			If you were spawned by another session (check `list_sessions` or just notice your prompt
			came with a specific, scoped task), you're a worker in someone else's orchestration:
			- Return concise markdown as your final response. Honor any `outputFormat` guidance in the
			  prompt. If your result is large or structured, write it to a file in your working
			  directory and return the path rather than inlining it.
			- Don't spawn further sub-sessions unless the task genuinely requires it — orchestration
			  depth is limited and nesting adds latency for no benefit on simple tasks.
			- You are headless and the CLI process is terminated between turns. Never use the CLI's
			  native `ScheduleWakeup`/`CronCreate`/`CronList`/`CronDelete` — those schedules won't
			  survive. Use `mcp__cmx__schedule_wake` if you need to poll or resume later.
			""";
	}

	/// <summary>Constants for Windows toast notifications (FR.16).</summary>
	public static class Notifications
	{
		/// <summary>Maximum characters of assistant response shown in the toast body.</summary>
		public const int MaxBodyChars = 200;

		/// <summary>Toast argument key for the NodeId used for click-to-activate routing.</summary>
		public const string ArgNodeId = "nodeId";

		/// <summary>Application User Model ID used to identify the app to the Windows toast system.</summary>
		public const string Aumid = "Duxre.TessynDesktop";
	}

	/// <summary>Constants for plan-usage polling via the Anthropic OAuth usage endpoint (FR.18).</summary>
	public static class Usage
	{
		public const string Endpoint            = "https://api.anthropic.com/api/oauth/usage";
		public const string BetaHeaderValue     = "oauth-2025-04-20";
		public const string CredentialsFileName = ".credentials.json";
		/// <summary>Default Claude credentials path for the system-wide (non-profiled) account.</summary>
		public const string DefaultClaudeRelativePath = ".claude";
		public const double PollingIntervalMinutes = 5.0;
		/// <summary>Severity value meaning "no elevated usage" — the API's own default and what a staleness-zeroed window is reset to (FR.18.8).</summary>
		public const string NormalSeverity = "normal";
	}

	/// <summary>Constants for local Ollama model discovery and routing (FR.12.13, FR.12.14).</summary>
	public static class Ollama
	{
		public const string DefaultBaseUrl    = "http://localhost:11434";
		public const string TagsPath          = "/api/tags";
		public const string ShowPath          = "/api/show";
		public const string AuthToken         = "ollama";
		public const int    DiscoveryTimeoutMs = 2500;
		public const int    ShowTimeoutMs      = 3000;
	}

	/// <summary>Constants for the External MCP Server Registry and its OAuth 2.1 client role (FR.20).</summary>
	public static class ExternalMcp
	{
		/// <summary>Folder under %AppData%\TessynDesktop holding DPAPI-protected per-server token files (FR.20.5).</summary>
		public const string TokenStoreFolderName = "mcp-tokens";
		public const string TokenFileExtension   = ".token";

		public const string WellKnownProtectedResourcePath   = "/.well-known/oauth-protected-resource";
		public const string WellKnownAuthorizationServerPath = "/.well-known/oauth-authorization-server";

		/// <summary>Loopback redirect path used for the interactive PKCE authorization flow (FR.20.4).</summary>
		public const string RedirectPath = "/callback";

		/// <summary>How long the interactive authorization flow waits for the browser redirect before giving up.</summary>
		public const int AuthorizeTimeoutMs = 120_000;

		/// <summary>Safety margin before actual expiry at which a token is proactively refreshed (FR.20.6).</summary>
		public const int TokenExpiryMarginSeconds = 60;

		/// <summary>
		/// Upper bound on <see cref="IMcpOAuthService.DiscoverAsync"/>'s well-known metadata probes.
		/// Independent of the shared <see cref="System.Net.Http.HttpClient"/>'s default 100s timeout —
		/// a server that never returns 404 for unknown paths (e.g. one that proxies every GET into its
		/// streamable-HTTP MCP handler) can otherwise hang the probe and buffer an unbounded response
		/// body in memory for the full 100s, and discovery re-fires on every URL-field focus loss with
		/// no de-dupe, compounding the leak per attempt.
		/// </summary>
		public const int DiscoveryTimeoutMs = 8_000;
	}


	public static class ClaudeSessions
	{
		public const string ClaudeHomeFolderName = ".claude";
		public const string ProjectsFolderName = "projects";
		public const string SessionFileExtension = ".jsonl";
		public const int StatusCheckIntervalSeconds = 60;
		public const int FirstPromptMaxLength = 500;
		public const int PromptSamplesCount = 3;

		/// <summary>
		/// Derives the project slug that Claude Code uses for its session storage path.
		/// All non-alphanumeric characters (except '-') are replaced with '-'.
		/// Trailing path separators are trimmed first.
		/// </summary>
		public static string BuildProjectSlug(string workingDirectory)
		{
			var trimmed = workingDirectory.TrimEnd('\\', '/');
			var chars = new char[trimmed.Length];
			for (var i = 0; i < trimmed.Length; i++)
			{
				var c = trimmed[i];
				chars[i] = char.IsLetterOrDigit(c) || c == '-' ? c : '-';
			}
			return new string(chars);
		}
	}
}
