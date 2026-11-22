using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Values of the <c>general</c> settings section. <c>SessionFilesRoot</c>, <c>ClaudePath</c> and <c>SourceCodesLocation</c> are read-only on the wire.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record GeneralSettingsDto(
	string SessionFilesRoot,
	string ClaudePath,
	string SourceCodesLocation,
	string HttpsProxy,
	string OllamaBaseUrl,
	bool AgentToolsEnabled,
	bool CodeReferencesEnabled,
	FileAccessSettingsDto FileAccess,
	IReadOnlyList<string> MediaPreviewHosts);
