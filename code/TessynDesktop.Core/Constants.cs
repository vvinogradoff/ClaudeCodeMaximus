using System;
using System.Collections.Generic;
using System.IO;

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

		/// <summary>
		/// Text content of a <c>tool_result</c> block (Read/Grep/Bash/WebFetch/Task-subagent output,
		/// etc.). Only emitted by <see cref="IClaudeSessionImportService.ParseJsonlSessionForIndexing"/>
		/// for search-index purposes — never rendered in the session view or exported .txt files.
		/// </summary>
		public const string RoleToolResult = "TOOL_RESULT";
		public const string TimestampFormat = "yyyy-MM-ddTHH:mm:ssZ";

		/// <summary>How many trailing .txt entries the JSONL write-back compares candidates against (ISSUE.014).</summary>
		public const int WriteBackTailWindow = 20;
	}

	public static class ContextRestore
	{
		public const string NoConversationFoundMarker = "No conversation found";
	}

	public static class Auth
	{
		/// <summary>Substring of the Claude CLI's 401 error text (FR.12.16).</summary>
		public const string InvalidCredentialsMarker = "Invalid authentication credentials";

		/// <summary>
		/// Substring of the Claude CLI's transient token-refresh-race error text (ISSUE.010): two
		/// <c>claude</c> processes sharing one <c>CLAUDE_CONFIG_DIR</c> can both attempt to refresh a
		/// near-expiry OAuth token at once — the loser presents an already-rotated refresh token and
		/// the CLI reports this, even though the account itself is not actually revoked.
		/// </summary>
		public const string OAuthRefreshRaceMarker = "OAuth session expired and could not be refreshed";

		/// <summary>
		/// Delay before the single auto-retry of a turn that failed with <see cref="OAuthRefreshRaceMarker"/>
		/// (ISSUE.010) — gives the concurrent process time to finish writing its refreshed credentials.
		/// </summary>
		public const int OAuthRefreshRaceRetryDelayMs = 1500;

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
			"`mcp__" + Agent.McpServerName + "__list_sessions` to find sessions (one page, newest first: your project, with your own workers under you; narrow with `query`, `running` or `scope`), " +
			"`mcp__" + Agent.McpServerName + "__orchestrate_parallel` for concurrent fan-out over a task array (returns all results together as one markdown document), " +
			"`mcp__" + Agent.McpServerName + "__orchestrate_pipeline` for pipelined fan-out where items flow through stages independently (no barrier; returns one markdown document), " +
			"`mcp__" + Agent.McpServerName + "__workflow_phase` to mark a named phase for progress tracking in the UI, " +
			"`mcp__" + Agent.McpServerName + "__workflow_log` to emit a progress message visible in the session tree, " +
			"`mcp__" + Agent.McpServerName + "__get_budget` to check remaining token budget for the current orchestration, and " +
			"`mcp__" + Agent.McpServerName + "__set_session_model` to switch the model of a child session mid-orchestration. " +
			"For detailed guidance on orchestrating multi-session workflows, or on how to respond if you were spawned as a worker by another session, use the `" + Agent.OrchestrationSkillName + "` skill.";

		/// <summary>
		/// FR.21.3 — Emitted once per fresh session (alongside <see cref="NoWorkflowTool"/>) when both
		/// agent tools and the Telegram Bridge are enabled. Deliberately steers Claude toward a
		/// fire-and-forget notification instead of any blocking "ask and wait" pattern, since there is
		/// no such tool — a mid-turn block on a Telegram reply has no reliable way to time out if the
		/// user is away from their phone or Telegram is unreachable (see FR.21.3 rationale).
		/// </summary>
		public const string TelegramNotifyGuidance =
			"A Telegram bridge is available: `critical` items, and items you post with `notify: true`, also ping the user's Telegram and desktop. Use `notify: true` only when the user must act soon — never for progress updates — and only from this top-level session, never from a worker or Task-tool subagent (report back to your supervisor instead). `mcp__" + Agent.McpServerName + "__notify_user` still sends a plain message and is also pinned as an ask, but prefer `post_attention`. There is no blocking \"ask and wait\" tool — after posting an ask, end your turn normally instead of waiting idle for a reply. The user's reply arrives later as an ordinary new prompt in this same session.";

		/// <summary>
		/// FR.21.24 — Emitted on every turn the scheduler started (not once per session), when agent
		/// tools and the Telegram Bridge are enabled and the session is not a Worker Session. Nothing
		/// tells the user such a turn finished, so it must ask for them explicitly, and only when needed.
		/// </summary>
		public const string ScheduledTurnNotifyReminder =
			"The scheduler started this turn, so the user may not be watching and is not told when it finishes. If — and only if — your reply needs the user to decide or act and it isn't pinned yet, post it with `mcp__" + Agent.McpServerName + "__post_attention` (`notify: true` if they must act soon). Don't restate items that are already pinned.";

		/// <summary>
		/// FR.23.10 — Emitted once per fresh session, with the same gate as <see cref="NoWorkflowTool"/> plus
		/// "not a Worker Session". Session policy: how and when to pin items in the user's Focus view.
		/// </summary>
		public const string AttentionGuidance =
			"The user has a Focus view beside this session's output. Pin anything they need there with `mcp__" + Agent.McpServerName + "__post_attention`: `critical` — they must know immediately (it notifies them at once); `ask` — you need their answer, decision or approval; `todo` — something only they can do; `alert` — worth knowing, no reply needed; `decided` — a call you made for them so work isn't blocked, which they may want to override. Write each item so it stands on its own: the user may read it days later without this conversation. Post each item once — it stays pinned until resolved, so don't restate pinned items in later replies; refer to them by id. When the user answers an item in conversation, or it no longer applies, call `mcp__" + Agent.McpServerName + "__resolve_attention`.";

		/// <summary>
		/// FR.24.6 — Emitted on a top-level session's first turn in each app run (not only fresh sessions, so
		/// long-running ones learn the tool too). The current tags are appended with
		/// <see cref="SessionTagCurrentTagsFormat"/> when the session already has some.
		/// </summary>
		public const string SessionTagGuidance =
			"This session shows tags under its name in Tessyn: the Linear issues, GitHub pull requests and GitHub repositories it works on, which the user opens from there. Keep them current with `mcp__" + Agent.McpServerName + "__tag_session`: tag an issue, PR or repository when this session starts working on it (pass the full URL), and don't tag things it only mentions. Finished work stays tagged; remove a tag only if it was added by mistake.";

		/// <summary>FR.24.6 — Suffix of <see cref="SessionTagGuidance"/>; arg 0 = the labels joined with ", ".</summary>
		public const string SessionTagCurrentTagsFormat = " Current tags: {0}.";

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

	/// <summary>Naming shared by the JSON sidecar files Tessyn keeps under its data root (FR.23.3, FR.24.5).</summary>
	public static class Sidecar
	{
		public const string TempExtension = ".tmp";
		public const string CorruptFileInfix = ".bad-";
		public const string CorruptTimestampFormat = "yyyyMMddHHmmss";
		public const string FileExtension = ".json";
	}

	/// <summary>Constants for Session Tags (FR.24).</summary>
	public static class SessionTags
	{
		/// <summary>Folder under the data root that holds one tag JSON per NodeId plus the workspace map (FR.24.5).</summary>
		public const string FolderName = "tags";
		public const string LinearWorkspacesFileName = "linear-workspaces.json";

		/// <summary>Linear key token inside a session name (FR.24.2).</summary>
		public const string NameTokenPattern = @"(?<![A-Za-z0-9])([A-Z][A-Z0-9]{0,9})-(\d{1,6})(?![A-Za-z0-9])";

		/// <summary>A short Linear key given to the tool (FR.24.4); the team is case-insensitive.</summary>
		public const string LinearKeyPattern = @"^([A-Za-z][A-Za-z0-9]{0,9})-(\d{1,6})$";

		/// <summary><c>owner/repo#n</c> (FR.24.4).</summary>
		public const string ShortPullRequestPattern = @"^([A-Za-z0-9][A-Za-z0-9-]*)/([A-Za-z0-9._-]+)#(\d+)$";

		/// <summary><c>owner/repo</c> (FR.24.4).</summary>
		public const string ShortRepositoryPattern = @"^([A-Za-z0-9][A-Za-z0-9-]*)/([A-Za-z0-9._-]+)$";

		public const string LinearHost = "linear.app";
		public const string GitHubHost = "github.com";
		public const string LinearIssueSegment = "issue";
		public const string GitHubPullSegment = "pull";
		public const string GitDirectorySuffix = ".git";

		// URLs and labels (FR.24.1)
		public const string LinearIssueUrlFormat = "https://linear.app/{0}/issue/{1}";
		public const string GitHubPullRequestUrlFormat = "https://github.com/{0}/{1}/pull/{2}";
		public const string GitHubRepositoryUrlFormat = "https://github.com/{0}/{1}";
		public const string PullRequestKeyFormat = "{0}/{1}#{2}";
		public const string RepositoryKeyFormat = "{0}/{1}";
		public const string PullRequestLabelFormat = "{1} PR #{2}";

		// Limits (FR.24.4)
		public const int MaxEntriesPerArray = 30;

		// Tool (FR.24.4)
		public const string ToolName = "tag_session";
		public const string ToolDescription =
			"Tag this session with the Linear issues, GitHub pull requests and GitHub repositories it works on. The user sees the tags under the session name and opens them in the browser from there. add/remove take full URLs (best) or short forms: HSE-230, owner/repo#215, owner/repo. Tag what this session is working on, not everything it mentions. Finished work stays tagged; remove only tags added by mistake. Call with no arguments to list the current tags.";

		// Result lines (FR.24.4); arg 0 = label, arg 1 = URL, or the echoed entry / team where noted
		public const string TaggedLineFormat = "Tagged {0} - {1}";
		public const string AlreadyTaggedLineFormat = "Already tagged {0}.";
		public const string RemovedLineFormat = "Removed {0}.";
		public const string NotTaggedInvalidLineFormat = "Not tagged: '{0}' - not a Linear issue, GitHub pull request or repository.";
		public const string NotTaggedUnknownTeamLineFormat = "Not tagged: '{0}' - unknown Linear team '{1}'; pass the full https://linear.app/<workspace>/issue/{2} URL once.";
		public const string NotRemovedNotTaggedLineFormat = "Not removed: '{0}' - not tagged.";
		public const string NotRemovedFromNameLineFormat = "Not removed: '{0}' - it comes from the session name; rename the session to drop it.";
		public const string NotRemovedInvalidLineFormat = "Not removed: '{0}' - not a Linear issue, GitHub pull request or repository.";
		public const string TagsLineFormat = "Tags: {0}.";
		public const string TagsNone = "none";
		public const string LabelSeparator = ", ";

		// SYSTEM entry (FR.24.4)
		public const string SystemEntryPrefix = "[Tags]";
		public const string SystemAddedFormat = "added {0}";
		public const string SystemRemovedFormat = "removed {0}";
		public const string SystemPartSeparator = "; ";
	}

	/// <summary>Constants for the Attention Ledger and the Focus view (FR.23).</summary>
	public static class Attention
	{
		/// <summary>Folder under the data root that holds one ledger JSON per NodeId (FR.23.3).</summary>
		public const string FolderName = "attention";
		public const string LedgerFileExtension = ".json";

		/// <summary>Prefix of an item id: <c>A{n}</c> (FR.23.2).</summary>
		public const string IdPrefix = "A";

		// ── post_attention limits (FR.23.4) ──
		public const int MaxItemsPerCall = 30;
		public const int MaxTitleLength = 200;
		public const int MaxBodyLength = 20_000;
		public const int MaxOptions = 10;
		public const int MaxOptionLength = 200;
		public const int MaxRecommendationLength = 1_000;
		public const int MaxKeyLength = 100;

		/// <summary>Maximum length of a <c>resolve_attention</c> note (FR.23.5).</summary>
		public const int MaxNoteLength = 2_000;

		// ── Pinned list (FR.23.11) ──
		public const int MaxPinnedLines = 20;
		public const int MaxPinnedTitleLength = 120;
		public const string TitleEllipsis = "...";

		/// <summary>Focus badge background for a Critical top kind (FR.23.1).</summary>
		public const string BadgeCriticalBackground = "#8B0000";

		/// <summary>Focus badge background for an Ask top kind (FR.23.1).</summary>
		public const string BadgeAskBackground = "#DAA520";

		public const string BadgeCriticalForeground = "#FFFFFF";
		public const string BadgeAskForeground = "#000000";


		/// <summary>Title given to a captured <c>notify_user</c> message that has no non-empty line (FR.23.7).</summary>
		public const string NotifyUserFallbackTitle = "Message from the session";

		// ── Tool names and descriptions (FR.23.4, FR.23.5) ──
		public const string PostToolName = "post_attention";
		public const string ResolveToolName = "resolve_attention";
		public const string ListToolName = "list_attention";

		public const string PostToolDescription =
			"Pin items in the user's Focus view, next to this session's output, where they stay visible until resolved. Kinds: critical (the user must know immediately — always notifies them), ask (you need their answer or approval), todo (something only they can do), alert (worth knowing, no reply needed), decided (a call you made for them that they may override). Write each body so it stands on its own. Post an item once: re-posting an open item with the same key or title updates it instead of adding a copy. Don't restate pinned items in later replies — refer to them by id (A1, A2, …). Set notify=true to also ping the user's Telegram and desktop, only when they must act soon. Top-level sessions only — a worker's call is refused; report to your supervisor instead.";

		public const string ResolveToolDescription =
			"Close a pinned Focus item: status=answered when the user answered it in conversation (note = their answer), status=resolved when it is done or no longer applies (note = why).";

		public const string ListToolDescription =
			"List this session's Focus items — open (default) or all — with ids, kinds, statuses and answers.";

		// ── Tool result texts (FR.23.4–FR.23.7, FR.23.9) ──
		public const string WorkerRefusalText = "Not posted - worker sessions can't post to the user's Focus view. Report to your supervisor instead.";
		public const string PostedLineFormat = "{0} posted ({1}).";
		public const string AlreadyPinnedLineFormat = "{0} already pinned ({1}) - updated, not re-posted.";
		public const string NotifiedLineFormat = "Notified: {0}.";
		public const string NotNotifiedLine = "Notification not sent - neither Telegram nor desktop notifications are enabled.";
		public const string PostClosingLine = "Pinned items stay in the user's Focus view until resolved - don't restate them; refer to them by id.";
		public const string ResolvedAnsweredFormat = "{0} marked answered.";
		public const string ResolvedFormat = "{0} resolved.";
		public const string AlreadyClosedFormat = "{0} is already {1}.";
		public const string UnknownItemFormat = "Unknown attention item '{0}'.";
		public const string NoOpenItemsText = "No open items.";
		public const string NoItemsText = "No items.";
		public const string PinnedAsSuffixFormat = " Pinned as {0}.";
		public const string AlreadyPinnedAsSuffixFormat = " Already pinned as {0}.";
		public const string ChannelTelegram = "Telegram";
		public const string ChannelDesktop = "desktop";
		public const string ChannelSeparator = ", ";
		public const string ListStatusOpen = "open";
		public const string ListStatusAll = "all";
		public const string ResolveStatusAnswered = "answered";
		public const string ResolveStatusResolved = "resolved";

		// ── Notification message (FR.23.9) ──
		public const string NotifySingleFormat = "{0} ({1}): {2}";
		public const string NotifyManyHeaderFormat = "{0} new items in the Focus view:";
		public const string NotifyManyLineFormat = "- {0} ({1}): {2}";
		public const string NotifyToastTitleFormat = "{0} / {1}";

		// ── Session-file SYSTEM entries (FR.23.8) ──
		public const string SystemCreatedFormat = "[Attention {0} ({1})] {2}";
		public const string SystemUpdatedFormat = "[Attention {0} ({1}) updated] {2}";
		public const string SystemClosedFormat = "[Attention {0} {1}]";
		public const string SystemDismissedByUserFormat = "[Attention {0} dismissed by the user]";
		public const string SystemOptionsPrefix = "Options: ";
		public const string SystemOptionsSeparator = " | ";
		public const string SystemRecommendationPrefix = "Recommendation: ";

		// ── Pinned list lines (FR.23.11) ──
		public const string PinnedHeaderLine =
			"Pinned in the user's Focus view - they can see these, so don't restate them; refer to them by id, and call `mcp__" + Agent.McpServerName + "__resolve_attention` when one is answered in conversation or no longer applies:";
		public const string PinnedItemLineFormat = "  - {0} {1}: {2}";
		public const string PinnedMoreLineFormat = "  - ...and {0} more (call mcp__" + Agent.McpServerName + "__list_attention)";
		public const string ClosuresLinePrefix = "Closed by the user in the Focus view since your last turn: ";
		public const string ClosureFormat = "{0} {1} \"{2}\" {3}";
		public const string ClosureDismissed = "dismissed without an answer";
		public const string ClosureSeparator = "; ";

		// ── Answers prompt (FR.23.15) ──
		public const string AnswersPromptHeader = "Answers from the Focus view:";
		public const string AnswersPromptLineFormat = "- {0} ({1}): {2}";
		public const string AnswersContinuationIndent = "  ";
	}

	/// <summary>UI literals of the Focus view (FR.23.12–FR.23.15): labels, formats, chip colour and pill sizes.</summary>
	public static class Focus
	{
		public const string EmptyStateText = "Nothing needs your attention in this session.";

		public const string SendAnswersFormat = "Send answers ({0})";
		public const string DecisionsHeaderFormat = "Decisions ({0})";
		public const string ClosedHeaderFormat = "Closed ({0})";
		public const string RecommendationButtonFormat = "Use recommendation: {0}";
		public const string AcceptedRecommendationFormat = "Accepted your recommendation: {0}";

		public const string CloseLabelDismiss = "Dismiss";
		public const string CloseTooltip = "Closes this item without sending anything to the session.";
		public const string ReplyButtonLabel = "Reply";
		public const string CloseLabelGotIt = "Got it";

		public const string OutcomeAnswered = "Answered";
		public const string OutcomeResolved = "Resolved";
		public const string OutcomeDismissed = "Dismissed";
		public const string OutcomeFormat = "{0}: {1}";

		public const string PostedTodayFormat = "HH:mm";
		public const string PostedOtherDayFormat = "yyyy-MM-dd HH:mm";

		/// <summary>Muted grey of the Decided kind chip (FR.23.14).</summary>
		public const string ChipDecidedBackground = "#707070";

		/// <summary>Minimum width of the badge / chip pill, in device-independent pixels (FR.23.13).</summary>
		public const double PillMinWidth = 16;

		/// <summary>Corner radius of the count badge (Focus switch, tree and Recent-dropdown rows): a rounded rectangle, not a circle.</summary>
		public const double BadgeCornerRadius = 3;

		/// <summary>Corner radius of the kind chip on a card (FR.23.14).</summary>
		public const double ChipCornerRadius = 8;

		public const double BadgeFontSize = 10;
		public const double ChipFontSize = 11;
	}

	/// <summary>UI literals of the Tag row (FR.24.7).</summary>
	public static class Tags
	{
		public const string TooltipFromName = "From the session name";
		public const string TooltipTaggedFormat = "Tagged by the session {0}";
		public const string TimeTodayFormat = "HH:mm";
		public const string TimeOtherDayFormat = "yyyy-MM-dd HH:mm";
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

		/// <summary>Rows per <c>list_sessions</c> page when the caller gives no <c>limit</c> (FR.15.13).</summary>
		public const int ListSessionsDefaultLimit = 30;

		/// <summary>Upper clamp for the <c>list_sessions</c> <c>limit</c> (FR.15.13).</summary>
		public const int ListSessionsMaxLimit = 200;

		/// <summary>Session names in <c>list_sessions</c> rows are cut to this many characters (FR.15.13).</summary>
		public const int ListSessionsNameMaxLength = 60;

		/// <summary>Minimum length of a printed Short Node Id (FR.15.13).</summary>
		public const int ShortNodeIdMinLength = 8;

		/// <summary>Minimum length of a NodeId prefix accepted by tool arguments (FR.15.13).</summary>
		public const int NodeIdPrefixMinLength = 6;

		/// <summary>Maximum matches listed when a NodeId prefix is ambiguous (FR.15.13).</summary>
		public const int AmbiguousNodeIdMaxMatches = 5;

		/// <summary>MCP JSON-RPC protocol version string. Must match what the claude CLI sends in its initialize request.</summary>
		public const string ProtocolVersion = "2025-11-25";

		/// <summary>Prefix prepended to scheduled turn prompts as a SYSTEM message.</summary>
		public const string ScheduledTurnSystemPrefix = "[Scheduled]";

		/// <summary>Note recorded on a schedule created via the Send button's right-click "delay send" popup.</summary>
		public const string DelayedSendNote = "Delayed send";

		/// <summary>Prefix used in async mailbox messages posted to the supervisor.</summary>
		public const string WorkerFinishedPrefix = "[Worker finished]";

		/// <summary>Note of a Mailbox Delivery carrying one worker result (FR.15.3). Arg 0 = worker name.</summary>
		public const string MailboxSingleNoteFormat = "Worker '{0}' result";

		/// <summary>Note of a Mailbox Delivery that merged several worker results (FR.15.12). Arg 0 = count, arg 1 = names in arrival order.</summary>
		public const string MailboxMergedNoteFormat = "Worker results ({0}): {1}";

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

			Other tools: `list_sessions` to find your workers before deciding new-vs-resume — with no
			arguments it lists your project's sessions newest first, with your own workers nested under
			your row; narrow or page with `query`, `running`, `activeWithinHours`, `scope`
			(`children`/`subtree` of a `nodeId`, or `all` projects) and `offset`, and pass the short ids
			it prints to any tool that takes a session id; `read_session` to pull a worker's transcript
			directly; `stop_session` to cancel a running turn; `set_session_model` to change a child's
			model mid-orchestration (tier-enforced); `workflow_phase`/`workflow_log` to mark progress visible in the session tree UI;
			`budgetTokens` on the fan-out tools plus `get_budget` to track token spend. Depth
			(`MaxOrchestrationDepth`) and concurrency (`MaxConcurrentWorkers`) limits are enforced —
			expect an error if you nest or fan out too aggressively.

			Notifying the user: worker results (async `send_to_session`) and wake-ups arrive as
			scheduled turns the user is not watching, and nothing tells the user that such a turn
			finished.
			- Post the ask once with `post_attention` (`notify: true` when the user must act soon), and
			  only when the user must decide or act — never to narrate progress or each worker's
			  arrival. It stays pinned in the user's Focus view, so don't restate it in later delivery
			  turns. Keep delivery turns short.
			- Don't arm polling or "fallback" self-wakes to collect async results: they arrive on
			  their own, and several results that arrive while you're busy are delivered together in
			  one turn. Use a self-wake only for a real deadline, or as a long-interval check on a
			  worker that may be stuck.

			## As a worker

			If your prompt is a specific, scoped task sent by another session, you're a worker in
			someone else's orchestration (the `You:` line of `list_sessions` confirms it — no need for
			a wider listing):
			- Return concise markdown as your final response. Honor any `outputFormat` guidance in the
			  prompt. If your result is large or structured, write it to a file in your working
			  directory and return the path rather than inlining it.
			- Don't spawn further sub-sessions unless the task genuinely requires it — orchestration
			  depth is limited and nesting adds latency for no benefit on simple tasks.
			- You are headless and the CLI process is terminated between turns. Never use the CLI's
			  native `ScheduleWakeup`/`CronCreate`/`CronList`/`CronDelete` — those schedules won't
			  survive. Use `mcp__cmx__schedule_wake` if you need to poll or resume later.
			- Do not call `notify_user` or `post_attention` — a worker session's call is dropped or
			  refused before it ever reaches the user (both are reserved for the top-level session the
			  user is actually talking to); put anything the user needs to see in your final response
			  to your supervisor instead.
			""";
	}

	/// <summary>
	/// Well-known project-structure folder names (FR.1.3). When a directory node's last path segment is one of
	/// these, its display label is the parent folder's name instead (e.g. "HardStop\Code" is shown as "HardStop").
	/// </summary>
	public static class WellKnownFolders
	{
		/// <summary>Source code folder.</summary>
		public const string Code = "Code";

		/// <summary>Documentation folder.</summary>
		public const string Docs = "Docs";

		/// <summary>Data folder.</summary>
		public const string Data = "Data";

		/// <summary>All well-known project folder names; matched case-insensitively.</summary>
		public static readonly string[] All = [Code, Docs, Data];
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

	/// <summary>Constants for the Session Completion Sound (FR.22).</summary>
	public static class SessionSound
	{
		/// <summary>Subfolder under the Windows install directory holding the stock sound library.</summary>
		public const string SoundLibrarySubfolder = "Media";

		/// <summary>File name (within <see cref="SoundLibrarySubfolder"/>) of the default completion sound.</summary>
		public const string DefaultSoundFileName = "Windows Notify.wav";

		/// <summary>Default per-session throttle window (3 minutes).</summary>
		public const int DefaultThrottleSeconds = 180;

		/// <summary>Search pattern used to enumerate the sound library.</summary>
		public const string SoundFileSearchPattern = "*.wav";
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

	/// <summary>
	/// Constants for the Local Tool Profile feature (FR.12.21) — the curated set of native tools/MCP
	/// servers supplied to the claude CLI for a directory whose effective model is a tool-capable
	/// local Ollama model (weak local models mis-call tools when handed the full default set).
	/// </summary>
	public static class LocalTools
	{
		/// <summary>Selectable native tool catalog for the Local Tool Profile picker.</summary>
		public static readonly string[] Catalog =
			["Read", "Write", "Edit", "Bash", "Glob", "Grep", "LS", "WebFetch", "WebSearch", "NotebookEdit", "TodoWrite"];

		/// <summary>Default lean native-tool set applied when a directory has no explicit LocalToolProfile.</summary>
		public static readonly string[] DefaultNativeTools = ["Read", "Write", "Edit", "Bash", "Glob", "Grep", "LS"];
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


	/// <summary>Constants for the Telegram Notification Bridge (FR.21).</summary>
	public static class Telegram
	{
		/// <summary>Bot API base URL. The bot token and method name are appended: "{BotApiBaseUrl}{token}/{method}".</summary>
		public const string BotApiBaseUrl = "https://api.telegram.org/bot";

		/// <summary>Long-poll timeout passed to getUpdates. The HTTP client timeout is set higher than this.</summary>
		public const int LongPollTimeoutSeconds = 30;

		/// <summary>File name for the DPAPI-protected bot token under %AppData%\TessynDesktop (FR.21.6).</summary>
		public const string TokenFileName = "telegram-token.dat";

		/// <summary>Max entries returned by the /open command, mirroring the read_session tool's default.</summary>
		public const int OpenCommandEntryCount = 20;

		/// <summary>Telegram's own forum topic name length limit.</summary>
		public const int MaxTopicNameLength = 128;

		/// <summary>
		/// Chunk size for mirrored messages (FR.21.9). Telegram's real per-message cap is 4096
		/// UTF-16 code units; this leaves headroom for role prefixes (e.g. "USER: ").
		/// </summary>
		public const int MaxMessageLength = 4000;

		/// <summary>Bot API file download base URL: "{BotApiFileBaseUrl}{token}/{file_path}" (FR.21.14).</summary>
		public const string BotApiFileBaseUrl = "https://api.telegram.org/file/bot";

		/// <summary>The cloud Bot API cannot download files larger than this (FR.21.14).</summary>
		public const long MaxBotDownloadBytes = 20L * 1024 * 1024;

		/// <summary>Folder under %AppData%\TessynDesktop holding downloaded inbound attachments, in yyyy-MM-dd subfolders (FR.21.14).</summary>
		public const string InboxFolderName = "telegram-inbox";

		/// <summary>Inbox files older than this are deleted when the bridge starts (FR.21.14).</summary>
		public const int InboxRetentionDays = 30;

		/// <summary>Timeout of the single extra poll made after a batch ending in a media group (FR.21.16).</summary>
		public const int AlbumFollowUpPollSeconds = 1;

		/// <summary>
		/// Pause after a topic rename that really changed the name (FR.21.18). Each rename posts a service
		/// message into the group, whose flood limit is about 20 messages/minute; 6 s keeps half of that
		/// budget for normal bridge traffic.
		/// </summary>
		public const int TopicRenameIntervalMs = 6000;

		/// <summary>Pause after an edit that Telegram answered with TOPIC_NOT_MODIFIED (no service message posted, FR.21.18).</summary>
		public const int TopicRenameNoOpIntervalMs = 1000;

		/// <summary>Topic name reconciliation saves settings after this many recorded names (FR.21.18).</summary>
		public const int TopicRenameSaveBatchSize = 20;
	}

	public static class TreeSearch
	{
		/// <summary>Queries starting with this prefix switch Exact Search on automatically (FR.1.14).</summary>
		public const string AutoExactPrefix = "https://";
		/// <summary>Queries shorter than this many characters switch Exact Search on automatically (FR.1.14).</summary>
		public const int AutoExactBelowLength = 8;
	}

	public static class ClaudeSessions
	{
		public const string ClaudeHomeFolderName = ".claude";
		public const string ProjectsFolderName = "projects";
		public const string SessionFileExtension = ".jsonl";
		public const int StatusCheckIntervalSeconds = 60;

		/// <summary>Quiet period after a session's last completed turn before it is re-indexed for search (FR.1.13).</summary>
		public const int LiveIndexDebounceSeconds = 2;
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

		/// <summary>
		/// Every Claude CLI home whose <c>projects/</c> folder may hold session JSONL for this app:
		/// the OS-default <c>~/.claude</c>, plus one per Claude account profile directory under
		/// <c>%AppData%/TessynDesktop/profiles/</c> — active profiles and ones removed via FR.12.18
		/// (renamed with a <c>_rem</c> suffix, directory otherwise untouched) alike, since each
		/// profile launches its <c>claude</c> processes with <c>CLAUDE_CONFIG_DIR</c> pointed at its
		/// own directory, so its sessions never land under <c>~/.claude</c>.
		/// </summary>
		public static IReadOnlyList<string> GetAllProjectsDirs()
		{
			var dirs = new List<string>();

			var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			dirs.Add(Path.Combine(userProfile, ClaudeHomeFolderName, ProjectsFolderName));

			var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			var profilesRoot = Path.Combine(appData, AppDataFolderName, ProfilesFolderName);
			if (Directory.Exists(profilesRoot))
			{
				foreach (var profileDir in Directory.GetDirectories(profilesRoot))
					dirs.Add(Path.Combine(profileDir, ProjectsFolderName));
			}

			return dirs;
		}

		/// <summary>
		/// Searches every directory from <paramref name="projectsDirs"/> (defaults to
		/// <see cref="GetAllProjectsDirs"/>) for <c>{slug}/{sessionId}.jsonl</c>, returning the first
		/// match. Used so a session's JSONL can still be located after its originating profile is
		/// removed (FR.12.18) — the tree entry and its own compact log always survive removal, but
		/// without this, any feature reading the live Claude JSONL directly (resumability, the raw
		/// Claude Session View, cross-profile import) could only ever find sessions under the
		/// OS-default <c>~/.claude</c>.
		/// </summary>
		public static string? FindExistingJsonlPath(
			string workingDirectory, string sessionId, IReadOnlyList<string>? projectsDirs = null)
		{
			var slug = BuildProjectSlug(workingDirectory);
			var fileName = sessionId + SessionFileExtension;

			foreach (var projectsDir in projectsDirs ?? GetAllProjectsDirs())
			{
				var candidate = Path.Combine(projectsDir, slug, fileName);
				if (File.Exists(candidate))
					return candidate;
			}

			return null;
		}
	}
}
