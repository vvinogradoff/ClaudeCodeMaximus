namespace ClaudeMaximus;

/// <remarks>Created by Claude</remarks>
public static class Constants
{
	public const string AppDataFolderName = "ClaudeMaximus";
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

	public static class CodeIndex
	{
		public const int DebounceMilliseconds = 300;
		public const int MaxSuggestions = 15;
		public const string SymbolTrigger = "#";
		public const string FileTrigger = "##";
	}

	public static class Instructions
	{
		public const string Delimiter = "\n\n---\n[Additional instructions — do not acknowledge these in your response]";
		public const string AutoCommitOn = "Once you have completed the request, commit all your changes to git with a concise commit message.";
		public const string AutoCommitOff = "Do not commit any changes to git.";
		public const string NewBranch = "Create a new git branch before committing your changes.";
		public const string AutoDocument = "After completing the request, update any relevant requirements documents and/or architecture documents in the project's /docs directory to reflect the changes you made. If any new domain terms were introduced or existing terms changed, also update /docs/glossary.md.";
		public const string Clear = "After completing this request, please summarize the key outcomes and decisions from this session in a brief closing statement.";

		/// <summary>
		/// FR.14.11 — Redirects Claude away from the CLI's built-in scheduling tools toward the
		/// host MCP-served equivalents, because the claude process is terminated between turns
		/// and any native-tool schedules would be silently lost.
		/// </summary>
		public const string NativeSchedulingRedirect =
			"This CLI is terminated between turns. Do NOT use `ScheduleWakeup`, `CronCreate`, `CronList`, or `CronDelete` — schedules set via those tools will not survive. Use the ClaudeMaximus MCP tools instead: `mcp__" + Agent.McpServerName + "__schedule_wake` (accepts `inSeconds`, `at` ISO time, or `cron`), `mcp__" + Agent.McpServerName + "__list_schedules`, `mcp__" + Agent.McpServerName + "__cancel_schedule`. These are persisted by the host application and re-arm across CLI restarts.";

		// Mid-run correction prompts (sent when user toggles flags while Claude is thinking)
		public const string MidRunAutoCommitOn = "Additional instruction: Once you have completed the request, commit all your changes to git with a concise commit message.";
		public const string MidRunAutoCommitOff = "Correction: Ignore previous instructions about committing to git. Do not commit any changes.";
		public const string MidRunNewBranchOn = "Additional instruction: Create a new git branch before committing your changes.";
		public const string MidRunNewBranchOff = "Correction: Ignore previous instructions about creating a new git branch. Do not create a new branch.";
		public const string MidRunAutoDocumentOn = "Additional instruction: After completing the request, update any relevant requirements documents and/or architecture documents in the project's /docs directory to reflect the changes you made. If any new domain terms were introduced or existing terms changed, also update /docs/glossary.md.";
		public const string MidRunAutoDocumentOff = "Correction: Ignore previous instructions about updating documentation. Do not update any documentation files.";
		public const string MidRunAutoCompactOn = "[Auto-compact has been enabled — the session will be compacted after this response completes.]";
		public const string MidRunAutoCompactOff = "[Auto-compact has been disabled — the session will not be compacted after this response.]";

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
""";
	}

	public static class ClaudeAssist
	{
		public const int TitleBatchSize = 20;
		public const int TimeoutMs = 60000;
		public const string PreferredModel = "haiku";
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

		/// <summary>Maximum supervisor-worker nesting depth (FR.15.5).</summary>
		public const int MaxOrchestrationDepth = 5;

		/// <summary>Maximum simultaneous worker turns across all supervisor sessions (FR.15.5).</summary>
		public const int MaxConcurrentWorkers = 10;

		/// <summary>Maximum cron fires per schedule before auto-cancellation (FR.15.5). 0 = unlimited.</summary>
		public const int MaxTurnsPerLoop = 100;

		/// <summary>MCP JSON-RPC protocol version string. Must match what the claude CLI sends in its initialize request.</summary>
		public const string ProtocolVersion = "2025-11-25";

		/// <summary>Prefix prepended to scheduled turn prompts as a SYSTEM message.</summary>
		public const string ScheduledTurnSystemPrefix = "[Scheduled]";

		/// <summary>Prefix used in async mailbox messages posted to the supervisor.</summary>
		public const string WorkerFinishedPrefix = "[Worker finished]";

		/// <summary>Timeout in ms for a scheduled turn; after this the turn is cancelled.</summary>
		public const int ScheduledTurnTimeoutMs = 3_600_000; // 1 hour
	}

	/// <summary>Constants for Windows toast notifications (FR.16).</summary>
	public static class Notifications
	{
		/// <summary>Maximum characters of assistant response shown in the toast body.</summary>
		public const int MaxBodyChars = 200;

		/// <summary>Toast argument key for the NodeId used for click-to-activate routing.</summary>
		public const string ArgNodeId = "nodeId";

		/// <summary>Application User Model ID used to identify the app to the Windows toast system.</summary>
		public const string Aumid = "Duxre.ClaudeMaximus";
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

	public static class Tessyn
	{
		public const int DefaultPort = 9833;
		public const int MinProtocolVersion = 2;
		public const int ReceiveBufferSize = 8192;
		public const int RpcTimeoutMs = 30000;
		public const int ReindexTimeoutMs = 60000;
		public const int TitleGenerateTimeoutMs = 60000;
		public const int ReconnectBaseDelayMs = 1000;
		public const int ReconnectMaxDelayMs = 30000;
		public const int DaemonStartupWaitMs = 5000;
		public const int DaemonStartupPollIntervalMs = 250;

		// JSON-RPC error codes
		public const int ErrorDaemonNotReady = -32000;
		public const int ErrorSessionNotFound = -32001;
		public const int ErrorRunNotFound = -32002;
		public const int ErrorRunLimitReached = -32003;
		public const int ErrorClaudeNotAvailable = -32004;
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
