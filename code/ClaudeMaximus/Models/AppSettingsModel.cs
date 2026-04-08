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

	/// <summary>FileName of the last selected session, restored on startup. Deprecated: prefer ActiveSessionExternalId.</summary>
	public string? ActiveSessionFileName { get; set; }

	/// <summary>ExternalId (daemon UUID) of the last selected session. Takes precedence over ActiveSessionFileName.</summary>
	public string? ActiveSessionExternalId { get; set; }

	/// <summary>Whether the tree panel is collapsed (auto-hidden).</summary>
	public bool IsTreePanelCollapsed { get; set; }

	/// <summary>
	/// Path to the ClaudeMaximus source codes root (solution directory).
	/// Used by self-update to find build output. Empty = auto-detect or skip.
	/// </summary>
	public string SourceCodesLocation { get; set; } = string.Empty;

	/// <summary>
	/// Selected Claude model index (0=Default, 1=Opus, 2=Sonnet, 3=Haiku).
	/// When 0 (Default), no --model flag is passed to the CLI.
	/// </summary>
	public int SelectedModelIndex { get; set; }

	/// <summary>
	/// Configurable keyboard shortcuts. Populated with platform-appropriate defaults
	/// on first load if missing.
	/// </summary>
	public KeyBindingsModel KeyBindings { get; set; } = KeyBindingsModel.CreateDefaults();

	/// <summary>Path to the tessyn daemon executable. Default: "tessyn" (assumes on PATH).</summary>
	public string TessynPath { get; set; } = "tessyn";

	/// <summary>Whether to auto-start the Tessyn daemon if not already running.</summary>
	public bool AutoStartDaemon { get; set; } = true;

	/// <summary>
	/// When true, use the Tessyn daemon for session operations (send, load, search).
	/// When false, use the legacy local process and file-based code paths.
	/// Default false during migration; set to true once daemon integration is verified.
	/// </summary>
	public bool UseTessynDaemon { get; set; }

	/// <summary>
	/// Permission mode for daemon-spawned Claude sessions.
	/// "default" = Claude asks for tool approval (may block in headless mode).
	/// "auto-approve" = all tools auto-approved (--dangerously-skip-permissions).
	/// </summary>
	public string DaemonPermissionMode { get; set; } = "auto-approve";

	/// <summary>
	/// Daemon profile name to use for run.send. Null = use daemon's default profile.
	/// Auto-detected on startup if the default profile is not authenticated.
	/// </summary>
	public string? DaemonProfile { get; set; }
}
