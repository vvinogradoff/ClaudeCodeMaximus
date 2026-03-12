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
}
