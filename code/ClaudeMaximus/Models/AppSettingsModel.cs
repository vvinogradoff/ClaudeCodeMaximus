using System.Collections.Generic;

namespace ClaudeMaximus.Models;

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
	public List<DirectoryNodeModel> Tree { get; set; } = [];

	public double AssistantFontSize { get; set; } = 13.0;
	public double AssistantMarkdownFontSize { get; set; } = 13.0;
	public double UserFontSize { get; set; } = 13.0;
	public double InputFontSize { get; set; } = 13.0;

	public string Theme { get; set; } = "Dark";
	public ThemeColorsModel LightColors { get; set; } = new();
	public ThemeColorsModel DarkColors { get; set; } = ThemeColorsModel.DefaultDark();

	/// <summary>FileName of the last selected session, restored on startup.</summary>
	public string? ActiveSessionFileName { get; set; }

	/// <summary>Whether the tree panel is collapsed (auto-hidden).</summary>
	public bool IsTreePanelCollapsed { get; set; }

	/// <summary>
	/// Path to the ClaudeMaximus source codes root (solution directory).
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

	/// <summary>Persisted schedules (FR.14.8). Scheduler re-arms timers from this list on startup.</summary>
	public List<ScheduleModel> Schedules { get; set; } = [];
}
