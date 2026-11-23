using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>
/// Patch of the <c>general</c> section: absent (or null) members are left alone, a list replaces the whole list. A member that is not
/// listed here is rejected by <c>settings.update</c>, so the patch can reach nothing outside the section. <c>sessionFilesRoot</c>, <c>claudePath</c> and
/// <c>sourceCodesLocation</c> are in <see cref="GeneralSettingsDto"/> (read-only) but not here: they name what the daemon writes and executes.
/// </summary>
/// <param name="CodexPath">FR.33.17: an absolute local path to a native <c>.exe</c>, or empty to find Codex. Applies to Codex hosts started after the change.</param>
/// <param name="CodexCatalogProfileId">FR.33.2: <c>codex-default</c> or a stored <c>codex_N</c>; empty clears it (automatic).</param>
/// <param name="CodexModelTiers">FR.33.14: the whole edited table, top row first (at most 200 rows; each match 1-64 characters, unique in any case; tier 0-4).</param>
/// <param name="ResetCodexModelTiers"><c>true</c> is <b>Reset to defaults</b>: the stored table is dropped so this build's defaults apply. Not combinable with <see cref="CodexModelTiers"/>.</param>
/// <remarks>Created by Claude</remarks>
public sealed record GeneralSettingsPatch(
	string? HttpsProxy = null,
	string? OllamaBaseUrl = null,
	bool? AgentToolsEnabled = null,
	bool? CodeReferencesEnabled = null,
	FileAccessSettingsPatch? FileAccess = null,
	IReadOnlyList<string>? MediaPreviewHosts = null,
	string? CodexPath = null,
	string? CodexCatalogProfileId = null,
	IReadOnlyList<CodexModelTierDto>? CodexModelTiers = null,
	bool? ResetCodexModelTiers = null);
