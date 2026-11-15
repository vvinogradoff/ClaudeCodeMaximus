using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>
/// Patch of the <c>general</c> section: absent (or null) members are left alone, a list replaces the whole list. A member that is not
/// listed here is rejected by <c>settings.update</c>, so the patch can reach nothing outside the section. <c>sessionFilesRoot</c>, <c>claudePath</c> and
/// <c>sourceCodesLocation</c> are in <see cref="GeneralSettingsDto"/> (read-only) but not here: they name what the daemon writes and executes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record GeneralSettingsPatch(
	string? HttpsProxy = null,
	string? OllamaBaseUrl = null,
	bool? AgentToolsEnabled = null,
	FileAccessSettingsPatch? FileAccess = null,
	IReadOnlyList<string>? MediaPreviewHosts = null);
