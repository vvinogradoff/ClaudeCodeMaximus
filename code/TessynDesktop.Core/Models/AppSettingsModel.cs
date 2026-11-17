using System;
using System.Collections.Generic;
using System.IO;

namespace TessynDesktop.Models;

/// <summary>
/// Root model for appsettings.json. Holds all persistent application state:
/// tree structure, settings values, and window layout.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AppSettingsModel
{
	/// <summary>Claude CLI profiles with separate authentication contexts.</summary>
	public List<ClaudeProfileModel> Profiles { get; set; } = [];

	/// <summary>
	/// Selected Claude CLI profile index. 0 = Default (no --profile flag).
	/// Indices 1..N map to Profiles[0..N-1]. Last virtual index = "New..." action.
	/// </summary>
	public int SelectedProfileIndex { get; set; }

	public string SessionFilesRoot { get; set; } = string.Empty;
	public string ClaudePath { get; set; } = "claude";
	public WindowStateModel Window { get; set; } = new();

	/// <summary>Always-on runtime diagnostics (logging verbosity, hang watchdog, metrics, heap capture).</summary>
	public DiagnosticsSettingsModel Diagnostics { get; set; } = new();
	public List<DirectoryNodeModel> Tree { get; set; } = [];

	public double AssistantFontSize { get; set; } = 13.0;
	public double AssistantMarkdownFontSize { get; set; } = 13.0;
	public double UserFontSize { get; set; } = 13.0;
	public double InputFontSize { get; set; } = 13.0;

	public string Theme { get; set; } = "Dark";
	public ThemeColorsModel LightColors { get; set; } = new();
	public ThemeColorsModel DarkColors { get; set; } = ThemeColorsModel.DefaultDark();

	/// <summary>FileName of the last selected session, restored on startup. Deprecated: prefer ActiveSessionExternalId.</summary>
	public string? ActiveSessionFileName { get; set; }

	/// <summary>ExternalId (daemon UUID) of the last selected session. Takes precedence over ActiveSessionFileName.</summary>
	public string? ActiveSessionExternalId { get; set; }

	/// <summary>Whether the tree panel is collapsed (auto-hidden).</summary>
	public bool IsTreePanelCollapsed { get; set; }

	/// <summary>Obsolete: the tag area mode is no longer remembered (FR.24.16: every opened session starts collapsed). Read and dropped (the setter discards it), so old settings files load, it is never written back.</summary>
	[System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
	public bool TagAreaExpanded { get => false; set { } }

	/// <summary>
	/// Path to the TessynDesktop source codes root (solution directory).
	/// Used by self-update to find build output. Empty = auto-detect or skip.
	/// </summary>
	public string SourceCodesLocation { get; set; } = string.Empty;

	/// <summary>
	/// Selected model ID (true model ID string, e.g. "claude-opus-4-7" or "gemma4:26b").
	/// Empty string = Default (no --model flag passed to CLI). Persisted globally as last-used;
	/// per-directory selection is on DirectoryNodeModel.SelectedModelId.
	/// </summary>
	public string SelectedModelId { get; set; } = string.Empty;

	/// <summary>
	/// Base URL for the local Ollama instance (FR.12.13).
	/// Used to discover available local models and to route claude CLI spawns for Ollama models.
	/// </summary>
	public string OllamaBaseUrl { get; set; } = Constants.Ollama.DefaultBaseUrl;

	/// <summary>
	/// Configurable keyboard shortcuts. Populated with platform-appropriate defaults
	/// on first load if missing.
	/// </summary>
	public KeyBindingsModel KeyBindings { get; set; } = KeyBindingsModel.CreateDefaults();

	/// <summary>
	/// HTTPS proxy URL for Claude CLI requests (e.g. http://127.0.0.1:8080).
	/// When set, HTTPS_PROXY and NODE_TLS_REJECT_UNAUTHORIZED=0 environment
	/// variables are injected into the spawned claude process.
	/// </summary>
	public string HttpsProxy { get; set; } = string.Empty;

	/// <summary>Directory containing images for the screensaver slideshow. Empty = black screen.</summary>
	public string ScreensaverDirectory { get; set; } = string.Empty;

	/// <summary>Inactivity timeout in seconds before the screensaver activates. 0 = disabled.</summary>
	public int ScreensaverTimeout { get; set; } = 120;

	/// <summary>Seconds between image transitions in the screensaver slideshow.</summary>
	public int ScreensaverSlideshowInterval { get; set; } = 10;

	/// <summary>
	/// Whether the in-process agent MCP server is enabled (FR.14.1).
	/// When true, all claude spawns receive --mcp-config giving access to scheduling and orchestration tools.
	/// </summary>
	public bool AgentToolsEnabled { get; set; } = true;

	/// <summary>
	/// Port for the loopback MCP HTTP server (FR.14.1). 0 = pick a free port at startup.
	/// Updated with the actual port after the server starts.
	/// </summary>
	public int AgentMcpPort { get; set; }

	/// <summary>
	/// Legacy home of the TDCP bearer token. The token now lives in a DPAPI-protected file (<c>ITdcpTokenStore</c>);
	/// this property is only read once at startup to migrate an existing token and is then set to null, so it is no longer
	/// written to appsettings.json (null values are omitted).
	/// </summary>
	public string? TdcpToken { get; set; }

	/// <summary>Persisted schedules (FR.14.8). Scheduler re-arms timers from this list on startup.</summary>
	public List<ScheduleModel> Schedules { get; set; } = [];

	/// <summary>
	/// Whether desktop toast notifications are shown for completed scheduled and orchestrated turns (FR.16.5).
	/// Default true — disable to suppress all result toasts.
	/// </summary>
	public bool NotificationsEnabled { get; set; } = true;

	/// <summary>
	/// External MCP Server Registry (FR.20.1): user-managed servers merged into every session's
	/// --mcp-config alongside the in-process cmx server. Independent of claude.ai's own MCP
	/// connector registrations (FR.20.8).
	/// </summary>
	public List<ExternalMcpServerModel> ExternalMcpServers { get; set; } = [];

	/// <summary>
	/// What remote (relay) clients may read with <c>file.read</c> and <c>file.preview</c>. The default is the strictest:
	/// only files a Focus item references. Loopback clients are not limited by it.
	/// </summary>
	public FileAccessSettingsModel FileAccess { get; set; } = new();

	/// <summary>
	/// Hosts whose images and videos a client loads without asking (decision A13); <c>*.example.com</c> covers subdomains. Served to
	/// the clients by <c>catalog.get</c> as <c>mediaPreviewHosts</c>.
	/// </summary>
	public List<string> MediaPreviewHosts { get; set; } = [.. Constants.FileAccess.DefaultMediaPreviewHosts];

	/// <summary>
	/// Master toggle for the Telegram Notification Bridge (FR.21). When false, the bridge does not
	/// start and <c>notify_user</c>/<c>ITelegramBridgeService</c> calls are no-ops. The bot token
	/// itself is never stored here — see the Telegram Token Store (FR.21.6).
	/// </summary>
	public bool TelegramEnabled { get; set; }

	/// <summary>
	/// Chat ID of the Topics-enabled Telegram supergroup the bridge posts to (FR.21.1). Every
	/// inbound update from any other chat is dropped.
	/// </summary>
	public long TelegramChatId { get; set; }

	/// <summary>
	/// Telegram numeric user IDs authorized to message the bot (FR.21.1). Messages from anyone
	/// else are logged and dropped, even inside <see cref="TelegramChatId"/>.
	/// </summary>
	public List<long> TelegramAllowedUserIds { get; set; } = [];

	/// <summary>
	/// Persisted NodeId → Telegram <c>message_thread_id</c> map (FR.21.1, FR.21.2). The reverse
	/// lookup used to route inbound messages is rebuilt in memory from this on
	/// <see cref="Services.ITelegramBridgeService.Start"/>.
	/// </summary>
	public Dictionary<string, int> TelegramTopicThreadIdsByNodeId { get; set; } = [];

	/// <summary>
	/// Persisted NodeId → last-applied (untruncated) Telegram topic name (FR.21.1), recorded at topic
	/// creation and by topic name reconciliation (FR.21.18). Telegram cannot return a topic's name, so
	/// this is the only record of what was applied. Remove an entry wherever its thread-id mapping is removed.
	/// </summary>
	public Dictionary<string, string> TelegramTopicNamesByNodeId { get; set; } = [];

	/// <summary>
	/// Last-processed Telegram <c>update_id</c> + 1 (FR.21.1), persisted so an app restart does not
	/// replay already-handled updates through <c>getUpdates</c>.
	/// </summary>
	public long TelegramUpdateOffset { get; set; }

	/// <summary>
	/// Telegram @handle (e.g. "@harmlessevil") prefixed onto <c>notify_user</c> pings (FR.21.10). Empty means no prefix. Never applied
	/// to mirrored conversation text (FR.21.9), only to pings, so routine back-and-forth doesn't page
	/// the user on every line.
	/// </summary>
	public string TelegramMentionHandle { get; set; } = string.Empty;

	/// <summary>What the Universal I/O Mirror posts to a session's topic (FR.21.22). Defaults to the final answer of each turn only.</summary>
	public TelegramMirrorMode TelegramMirrorMode { get; set; } = TelegramMirrorMode.AnswersOnly;

	/// <summary>
	/// Path to the .wav file played by the Session Completion Sound (FR.22.4). Default is a
	/// stock Windows sound; any file under the Windows sound library (or elsewhere) may be chosen.
	/// </summary>
	public string SessionCompletionSoundFile { get; set; } = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.Windows),
		Constants.SessionSound.SoundLibrarySubfolder,
		Constants.SessionSound.DefaultSoundFileName);

	/// <summary>
	/// Minimum seconds between Session Completion Sound plays for the same session (FR.22.3).
	/// Default 180 (3 minutes).
	/// </summary>
	public int SessionCompletionSoundThrottleSeconds { get; set; } = Constants.SessionSound.DefaultThrottleSeconds;
}
