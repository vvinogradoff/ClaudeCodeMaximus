using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TessynDesktop;

/// <remarks>Created by Claude</remarks>
public static partial class Constants
{
	/// <summary>The Codex engine (FR.33): the <c>codex app-server</c> host, its JSON-RPC protocol and the texts it produces.</summary>
	public static class Codex
	{
		/// <summary>The Codex version this build was tested with (FR.33.6); compared on major and minor.</summary>
		public const string TestedVersion = "0.171";

		/// <summary>How long a host with no turn in progress and no thread loaded stays up (FR.33.6).</summary>
		public static readonly TimeSpan HostIdleShutdown = TimeSpan.FromMinutes(15);

		/// <summary>How long a control request (not a turn: <c>turn/start</c> answers at once) may go unanswered before it fails.</summary>
		public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

		/// <summary>How long a host is given to exit after its stdin closes before it is killed.</summary>
		public static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(5);

		/// <summary>How long the host waits for a crashed process's exit code and stderr before it reports the crash.</summary>
		public static readonly TimeSpan CrashReportWait = TimeSpan.FromSeconds(5);

		/// <summary>How many of the last stderr lines of a host are kept for the crash report (FR.33.6).</summary>
		public const int StderrTailLines = 50;

		/// <summary>Bytes read from the host's stdout per read call. Lines themselves have no length limit (FR.33.6).</summary>
		public const int ReadChunkBytes = 64 * 1024;

		/// <summary>A line buffer that grew past this is dropped after the line, so one huge line does not pin its memory.</summary>
		public const int LineBufferRetainBytes = 1024 * 1024;

		/// <summary>Longest one-line tool summary (FR.33.11); longer ones end with <see cref="Ellipsis"/>.</summary>
		public const int ToolSummaryMaxLength = 160;
		public const string Ellipsis = "...";

		/// <summary>
		/// The one serializer setup for the Codex protocol: camelCase members, nulls left out (an omitted <c>effort</c> means Default, FR.33.8).
		/// Shared so its metadata cache is built once.
		/// </summary>
		public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		};

		/// <summary>How long Stop waits for <c>turn/completed</c> after <c>turn/interrupt</c> before it ends the turn as cancelled (FR.33.8).</summary>
		public static readonly TimeSpan StopConfirmTimeout = TimeSpan.FromSeconds(15);

		/// <summary>Codex profiles (FR.33.5).</summary>
		public static class Profiles
		{
			/// <summary>The User's own <c>%USERPROFILE%\.codex</c>, used without <c>CODEX_HOME</c>.</summary>
			public const string DefaultProfileId = "codex-default";
		}

		/// <summary>The keys of the per-thread <c>config</c> of <c>thread/start</c> and <c>thread/resume</c> (FR.33.7, FR.33.9, FR.33.16).</summary>
		public static class Config
		{
			public const string McpServers = "mcp_servers";
			public const string Url = "url";
			public const string HttpHeaders = "http_headers";
			public const string Command = "command";
			public const string Args = "args";
			public const string Env = "env";
			public const string StartupTimeoutSec = "startup_timeout_sec";
			public const string ToolTimeoutSec = "tool_timeout_sec";
			public const string DefaultToolsApprovalMode = "default_tools_approval_mode";
			public const string ApprovalModeApprove = "approve";
			public const string ProjectDocFallbackFilenames = "project_doc_fallback_filenames";
			public const string ProjectDocFallbackClaude = "CLAUDE.md";
			public const string FeaturesApps = "features.apps";

			/// <summary>The cmx server: up to 20 s to start, and orchestration calls may block for an hour (FR.15.1 <c>wait</c> mode).</summary>
			public const int CmxStartupTimeoutSec = 20;
			public const int CmxToolTimeoutSec = 3600;

			/// <summary>What a redacted secret is shown as in the logs and in <c>ToString</c>.</summary>
			public const string Redacted = "<redacted>";

			/// <summary>The member names of a merged registry entry (the Claude <c>--mcp-config</c> shape) that are read when it is turned into Codex form.</summary>
			public const string RegistryType = "type";
			public const string RegistryTypeStdio = "stdio";
			public const string RegistryHeaders = "headers";
			public const string RegistryEnv = "env";
		}

		/// <summary>Process launch (FR.33.6, FR.33.5).</summary>
		public static class Launch
		{
			public const string AppServerArgument = "app-server";
			public const string VersionArgument = "--version";
			public const string ConfigOverrideArgument = "-c";
			public const string CodexHomeVariable = "CODEX_HOME";

			/// <summary>Keeps a stored profile's login in its own <c>auth.json</c>, never in the shared Windows credential store (FR.33.5).</summary>
			public const string FileCredentialStoreOverride = "cli_auth_credentials_store=\"file\"";

			/// <summary>The Codex Default profile's home under the user profile folder (FR.33.5).</summary>
			public const string DefaultHomeFolderName = ".codex";

			public const string ExecutableExtension = ".exe";

			/// <summary>Shims that must never be launched (FR.33.17): Tessyn launches only the native exe.</summary>
			public static readonly string[] ShimExtensions = [".cmd", ".bat", ".ps1", ".js", ".mjs", ".cjs"];
		}

		/// <summary>Where Codex is looked for, in this order (FR.33.17).</summary>
		public static class Discovery
		{
			public const string LocalAppDataVariable = "LOCALAPPDATA";
			public const string AppDataVariable = "APPDATA";
			public const string PathVariable = "PATH";
			public const string ExecutableFileName = "codex.exe";

			/// <summary>The official installer, under <c>%LOCALAPPDATA%</c>.</summary>
			public const string OfficialInstallerRelativePath = @"Programs\OpenAI\Codex\bin\codex.exe";

			/// <summary>The winget link, under <c>%LOCALAPPDATA%</c>: a symbolic link to the native exe in the winget package folder.</summary>
			public const string WingetLinkRelativePath = @"Microsoft\WinGet\Links\codex.exe";

			/// <summary>Folders of a global npm install (under <c>%APPDATA%</c>) that hold the native exe somewhere below them (nested and hoisted layouts).</summary>
			public static readonly string[] NpmNativePackageRelativePaths =
			[
				@"npm\node_modules\@openai\codex\node_modules\@openai\codex-win32-x64",
				@"npm\node_modules\@openai\codex-win32-x64",
			];

			/// <summary>What <c>codex --version</c> prints before the version, for example <c>codex-cli 0.171.0</c>.</summary>
			public const string VersionOutputPrefix = "codex-cli";

			public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);
		}

		/// <summary>The handshake (FR.33.6).</summary>
		public static class Client
		{
			public const string Name = "tessyn_desktop";
			public const string Title = "Tessyn";
		}

		/// <summary>Values Tessyn sends on <c>thread/start</c> and <c>turn/start</c> (FR.33.7, FR.33.8).</summary>
		public static class Values
		{
			public const string ApprovalPolicyNever = "never";
			public const string SandboxDangerFullAccess = "danger-full-access";
			public const string ServiceName = "tessyn";
			public const string ThreadSource = "tessyn";
			public const string SummaryConcise = "concise";
			public const string LoginTypeChatGpt = "chatgpt";
			public const string LoginTypeChatGptDeviceCode = "chatgptDeviceCode";
			public const string UserInputText = "text";
			public const string UserInputLocalImage = "localImage";
			public const string DecisionAccept = "accept";
			public const string ElicitationDecline = "decline";
			public const string PermissionScopeTurn = "turn";
		}

		/// <summary>JSON-RPC method names of the stable subset Tessyn uses.</summary>
		public static class Methods
		{
			public const string Initialize = "initialize";
			public const string Initialized = "initialized";

			public const string AccountRead = "account/read";
			public const string AccountLoginStart = "account/login/start";
			public const string AccountLoginCancel = "account/login/cancel";
			public const string AccountLoginCompleted = "account/login/completed";
			public const string AccountLogout = "account/logout";
			public const string AccountUpdated = "account/updated";
			public const string AccountRateLimitsRead = "account/rateLimits/read";
			public const string AccountRateLimitsUpdated = "account/rateLimits/updated";
			public const string ModelList = "model/list";

			public const string ThreadStart = "thread/start";
			public const string ThreadResume = "thread/resume";
			public const string ThreadNameSet = "thread/name/set";
			public const string ThreadNameUpdated = "thread/name/updated";
			public const string ThreadUnsubscribe = "thread/unsubscribe";
			public const string ThreadCompactStart = "thread/compact/start";
			public const string ThreadStarted = "thread/started";
			public const string ThreadClosed = "thread/closed";
			public const string ThreadTokenUsageUpdated = "thread/tokenUsage/updated";

			public const string TurnStart = "turn/start";
			public const string TurnInterrupt = "turn/interrupt";
			public const string TurnStarted = "turn/started";
			public const string TurnCompleted = "turn/completed";
			public const string TurnPlanUpdated = "turn/plan/updated";

			public const string ItemStarted = "item/started";
			public const string ItemCompleted = "item/completed";
			public const string AgentMessageDelta = "item/agentMessage/delta";
			public const string ReasoningSummaryTextDelta = "item/reasoning/summaryTextDelta";

			public const string Error = "error";
			public const string McpServerStartupStatusUpdated = "mcpServer/startupStatus/updated";

			public const string CommandExecutionRequestApproval = "item/commandExecution/requestApproval";
			public const string FileChangeRequestApproval = "item/fileChange/requestApproval";
			public const string PermissionsRequestApproval = "item/permissions/requestApproval";
			public const string McpServerElicitationRequest = "mcpServer/elicitation/request";
		}

		/// <summary>JSON-RPC wire members and error codes.</summary>
		public static class Rpc
		{
			public const string Id = "id";
			public const string Method = "method";
			public const string Params = "params";
			public const string Result = "result";
			public const string Error = "error";
			public const string Code = "code";
			public const string Message = "message";
			public const string Data = "data";
			public const string ThreadId = "threadId";
			public const string TurnId = "turnId";
			public const string Turn = "turn";

			public const int InvalidRequest = -32600;
			public const int MethodNotFound = -32601;
			public const int InternalError = -32603;
		}

		/// <summary>The <c>-32600</c> messages that tell thread failures apart (FR.33.7); Codex sends no structured code for them.</summary>
		public static class ThreadErrorMessages
		{
			public const string NoRollout = "no rollout found for thread id";
			public const string InvalidSessionId = "invalid session id";
			public const string ActiveWriter = "already has an active writer";
			public const string ThreadNotFound = "thread not found";
		}

		/// <summary>Values of the protocol's open string unions that Tessyn reacts to.</summary>
		public static class Protocol
		{
			public const string ItemAgentMessage = "agentMessage";
			public const string ItemUserMessage = "userMessage";
			public const string ItemReasoning = "reasoning";
			public const string ItemCommandExecution = "commandExecution";
			public const string ItemFileChange = "fileChange";
			public const string ItemMcpToolCall = "mcpToolCall";
			public const string ItemWebSearch = "webSearch";
			public const string ItemImageView = "imageView";
			public const string ItemContextCompaction = "contextCompaction";

			public const string TurnCompleted = "completed";
			public const string TurnInterrupted = "interrupted";
			public const string TurnFailed = "failed";

			public const string ItemStatusCompleted = "completed";
			public const string PlanStepCompleted = "completed";
			public const string PlanStepInProgress = "inProgress";
			public const string PlanStepPending = "pending";

			public const string ErrorUnauthorized = "unauthorized";
			public const string ErrorUsageLimitExceeded = "usageLimitExceeded";
			public const string ErrorRateLimitExceeded = "rateLimitExceeded";
			public const string ErrorContextWindowExceeded = "contextWindowExceeded";
		}

		/// <summary>Texts the Codex engine shows or writes (FR.33.8, FR.33.11, FR.33.13).</summary>
		public static class Texts
		{
			public const string InterruptedMarker = "[interrupted]";

			/// <summary>FR.33.8: Stop sent <c>turn/interrupt</c> but <c>turn/completed</c> did not come within 15 s.</summary>
			public const string StopNotConfirmed = "Stop did not confirm within 15 s; the Codex turn may still be finishing.";

			/// <summary>FR.33.7: another Codex process on the same <c>CODEX_HOME</c> has the thread open.</summary>
			public const string ActiveWriter = "This conversation is open in another Codex window. Close it there and send again.";

			/// <summary>FR.33.6: the app-server exited during the turn. Arg 0 = the exit code, or <c>unknown</c>.</summary>
			public const string HostStoppedFormat = "Codex stopped unexpectedly (exit {0}). Send again to continue.";
			public const string UnknownExitCode = "unknown";

			/// <summary>A Codex call failed before the turn ran (not found, a timeout, a protocol error). Arg 0 = the reason.</summary>
			public const string StartFailedFormat = "Codex could not start the turn: {0}";

			/// <summary>FR.33.12: <c>thread/compact/start</c> finished; the thread is kept.</summary>
			public const string Compacted = "[Session compacted by Codex — the conversation continues on the same thread]";

			/// <summary>FR.33.12: Compact now on a thread Codex no longer has; the next turn starts a new one from the transcript.</summary>
			public const string CompactThreadGone = "the Codex thread is gone; the next prompt starts a new one from the transcript";

			/// <summary>The turn ended as interrupted without a Stop from Tessyn.</summary>
			public const string InterruptedElsewhere = "The Codex turn was interrupted.";
			public const string ErrorFormat = "Codex error: {0}";
			public const string ErrorWithCodeFormat = "Codex error: {0} ({1})";
			public const string ContextWindowExceeded = "Codex error: the conversation is longer than the model's context window. Compact the session (Compact now) and send again.";
			public const string RetryingFormat = "Codex is retrying: {0}";
			public const string UnmappedItemFormat = "Codex: {0}";
			public const string PlanFormat = "Plan: {0}/{1} - {2}";
			public const string PlanNoStepFormat = "Plan: {0}/{1}";
			public const string CommandFormat = "$ {0}";
			public const string CommandFailedFormat = "$ {0} (exit {1})";
			public const string FileChangeFormat = "edit {0} file(s): {1}";
			public const string FileChangeNoPathFormat = "edit {0} file(s)";
			public const string McpToolFormat = "{0}.{1}";
			public const string WebSearchFormat = "search \"{0}\"";
			public const string ImageViewFormat = "view {0}";
			public const string UnsupportedServerRequestFormat = "Tessyn does not support {0}";
			public const string NotFound = "Codex not found. Install it with: winget install OpenAI.Codex";
			public const string ConfiguredPathMissingFormat = "The Codex path {0} does not exist.";
			public const string ConfiguredPathShimFormat = "The Codex path {0} is a script shim; set the native codex.exe instead.";
			public const string FoundFormat = "Codex {0} found at {1}";
			public const string UntestedVersionFormat = "Codex {0} is newer than the tested {1}. If GPT sessions misbehave, install {1}.";
		}
	}
}
