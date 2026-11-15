using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Values of the <c>general</c> settings section.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record GeneralSettingsDto(
	string SessionFilesRoot,
	string ClaudePath,
	string SourceCodesLocation,
	string HttpsProxy,
	string OllamaBaseUrl,
	bool AgentToolsEnabled,
	FileAccessSettingsDto FileAccess,
	IReadOnlyList<string> MediaPreviewHosts);
