using System.Collections.Generic;

namespace ClaudeMaximus.Models;

/// <summary>
/// Root model for appsettings.json. Holds all persistent application state:
/// tree structure, settings values, and window layout.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class AppSettingsModel
{
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
}
