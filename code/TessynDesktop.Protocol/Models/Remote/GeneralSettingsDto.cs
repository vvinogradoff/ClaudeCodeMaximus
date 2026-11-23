using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Values of the <c>general</c> settings section. <c>SessionFilesRoot</c>, <c>ClaudePath</c> and <c>SourceCodesLocation</c> are read-only on the wire.</summary>
/// <param name="CodexPath">The configured <c>codex.exe</c> (FR.33.17); empty = find it. Additive (tdcp-spec 10.A14).</param>
/// <param name="CodexStatus">Read-only: where Codex was found and its version, read at the time of <c>settings.get</c>. Omitted when the daemon has no Codex engine; never part of the section version.</param>
/// <param name="CodexCatalogProfileId">The ChatGPT profile the GPT model list is read from (FR.33.2); omitted = automatic (Codex Default, else the first signed-in one).</param>
/// <param name="CodexModelTiers">The GPT model tier table in use, top row first (FR.33.14): the edited one, or this build's defaults.</param>
/// <param name="CodexModelTiersAreDefaults">True while no edited table is stored, so <see cref="CodexModelTiers"/> are this build's defaults.</param>
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
	IReadOnlyList<string> MediaPreviewHosts,
	string CodexPath = "",
	CodexStatusDto? CodexStatus = null,
	string? CodexCatalogProfileId = null,
	IReadOnlyList<CodexModelTierDto>? CodexModelTiers = null,
	bool CodexModelTiersAreDefaults = true);
