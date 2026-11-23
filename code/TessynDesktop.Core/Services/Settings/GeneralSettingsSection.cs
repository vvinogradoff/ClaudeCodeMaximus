using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Codex;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The <c>general</c> section: session files root, Claude path, source tree, proxy, Ollama URL, agent tools, remote file access and the media
/// preview hosts. The whitelist is the member list of <see cref="GeneralSettingsPatch"/> and <see cref="FileAccessSettingsPatch"/>.
/// The session files root, the Claude path and the source tree are <b>read-only</b> here (decision U1): the first is where files are written and the
/// other two name what the daemon executes, so they are edited in the Avalonia Settings window only.
/// FR.33.17 (tdcp-spec 10.A14) adds the Codex members: <c>codexPath</c> (writable: Settings &gt; General edits it and the section is loopback only),
/// <c>codexCatalogProfileId</c>, the GPT tier table with Reset to defaults, and the read-only <c>codexStatus</c>, a live value filled by
/// <see cref="AddLiveValuesAsync"/> only when the section has the Codex settings service.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class GeneralSettingsSection : ISettingsSection
{
	[GeneratedRegex(@"^(\*\.)?[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$")]
	private static partial Regex HostPattern();

	private readonly CodexSettingsService? _codex;

	/// <param name="codex">Reads the Codex status for <c>codexStatus</c>; without it the member is omitted (a daemon with no Codex engine).</param>
	public GeneralSettingsSection(CodexSettingsService? codex = null)
	{
		_codex = codex;
	}

	/// <inheritdoc />
	public string Name => RemoteProtocol.SettingsApi.GeneralSection;

	/// <inheritdoc />
	public object Read(AppSettingsModel settings)
	{
		var access = settings.FileAccess ?? new FileAccessSettingsModel();
		return new GeneralSettingsDto(
			settings.SessionFilesRoot,
			settings.ClaudePath,
			settings.SourceCodesLocation,
			settings.HttpsProxy,
			settings.OllamaBaseUrl,
			settings.AgentToolsEnabled,
			settings.CodeReferencesEnabled,
			new FileAccessSettingsDto(
				(FileAccessModeDto)(int)access.Mode, access.MaxRelayChunkBytes, access.MaxTextFileBytes,
				access.MaxImageFileBytes, access.MaxPdfFileBytes, access.MaxVideoFileBytes),
			[.. settings.MediaPreviewHosts],
			settings.CodexPath,
			CodexStatus: null,
			string.IsNullOrEmpty(settings.CodexCatalogProfileId) ? null : settings.CodexCatalogProfileId,
			[.. (settings.CodexModelTiers?.Select(r => (r.Match, r.Tier)) ?? Constants.Codex.Tiers.Defaults).Select(r => new CodexModelTierDto(r.Match, r.Tier))],
			settings.CodexModelTiers == null);
	}

	/// <inheritdoc />
	public async Task<object> AddLiveValuesAsync(object values, CancellationToken cancellationToken)
	{
		if (_codex == null || values is not GeneralSettingsDto dto)
			return values;

		var status = await _codex.GetStatusAsync(cancellationToken).ConfigureAwait(false);
		return dto with
		{
			CodexStatus = new CodexStatusDto(status.Found, status.ExecutablePath, status.Version, status.TestedVersion, status.Note, status.StatusLine),
		};
	}

	/// <inheritdoc />
	public Action CaptureRestore(AppSettingsModel settings)
	{
		var (proxy, ollama, agentTools, codeRefs, hosts) = (settings.HttpsProxy, settings.OllamaBaseUrl, settings.AgentToolsEnabled, settings.CodeReferencesEnabled, settings.MediaPreviewHosts);
		var (codexPath, catalogProfile, tiers) = (settings.CodexPath, settings.CodexCatalogProfileId, settings.CodexModelTiers);
		var hadAccess = settings.FileAccess;
		var access = hadAccess == null ? ((FileAccessMode, int, long, long, long, long)?)null
			: (hadAccess.Mode, hadAccess.MaxRelayChunkBytes, hadAccess.MaxTextFileBytes, hadAccess.MaxImageFileBytes, hadAccess.MaxPdfFileBytes, hadAccess.MaxVideoFileBytes);
		return () =>
		{
			(settings.HttpsProxy, settings.OllamaBaseUrl, settings.AgentToolsEnabled, settings.CodeReferencesEnabled, settings.MediaPreviewHosts) = (proxy, ollama, agentTools, codeRefs, hosts);
			(settings.CodexPath, settings.CodexCatalogProfileId, settings.CodexModelTiers) = (codexPath, catalogProfile, tiers);
			settings.FileAccess = hadAccess;
			if (hadAccess != null && access is { } v)
				(hadAccess.Mode, hadAccess.MaxRelayChunkBytes, hadAccess.MaxTextFileBytes, hadAccess.MaxImageFileBytes, hadAccess.MaxPdfFileBytes, hadAccess.MaxVideoFileBytes) = v;
		};
	}

	/// <inheritdoc />
	public void Apply(AppSettingsModel settings, JsonNode patch)
	{
		var p = SettingsPatchReader.Read<GeneralSettingsPatch>(patch);

		// Validate everything first so that a rejected patch changes nothing.
		var proxy            = p.HttpsProxy == null ? null : SettingsPatchReader.HttpUrl(nameof(p.HttpsProxy), p.HttpsProxy, allowEmpty: true);
		var ollama           = p.OllamaBaseUrl == null ? null : SettingsPatchReader.HttpUrl(nameof(p.OllamaBaseUrl), p.OllamaBaseUrl, allowEmpty: false);
		var hosts            = p.MediaPreviewHosts == null ? null : ReadHosts(p.MediaPreviewHosts);
		var access           = p.FileAccess == null ? null : ValidateAccess(p.FileAccess);
		var codexPath        = p.CodexPath == null ? null : ReadCodexPath(p.CodexPath);
		var catalogProfile   = p.CodexCatalogProfileId == null ? null : ReadCatalogProfile(settings, p.CodexCatalogProfileId);
		var tiers            = p.CodexModelTiers == null ? null : ReadTiers(p.CodexModelTiers);
		if (tiers != null && p.ResetCodexModelTiers == true)
			throw new TdcpInvalidParamsException($"'{nameof(GeneralSettingsPatch.CodexModelTiers)}' and '{nameof(GeneralSettingsPatch.ResetCodexModelTiers)}' cannot be combined");

		if (proxy != null)            settings.HttpsProxy = proxy;
		if (ollama != null)           settings.OllamaBaseUrl = ollama;
		if (p.AgentToolsEnabled is { } agentTools) settings.AgentToolsEnabled = agentTools;
		if (p.CodeReferencesEnabled is { } codeRefs) settings.CodeReferencesEnabled = codeRefs;
		if (hosts != null)            settings.MediaPreviewHosts = hosts;
		if (access != null)
			ApplyAccess(settings, access);
		if (codexPath != null)        settings.CodexPath = codexPath;
		if (catalogProfile != null)   settings.CodexCatalogProfileId = catalogProfile.Length == 0 ? null : catalogProfile;
		if (tiers != null)            settings.CodexModelTiers = tiers;
		if (p.ResetCodexModelTiers == true) settings.CodexModelTiers = null;
	}

	/// <summary>FR.33.17: empty (find Codex) or a fully qualified local path to a native <c>.exe</c>; whether it exists shows in <c>codexStatus</c>.</summary>
	private static string ReadCodexPath(string value)
	{
		var name = nameof(GeneralSettingsPatch.CodexPath);
		var path = SettingsPatchReader.AbsolutePath(name, value, allowEmpty: true);
		if (path.Length > 0 && !path.EndsWith(Constants.SettingsApi.ExeExtension, StringComparison.OrdinalIgnoreCase))
			throw new TdcpInvalidParamsException($"'{name}' must name the native codex.exe, not a script or shim");

		return path;
	}

	/// <summary>FR.33.2: empty (automatic), Codex Default or a stored ChatGPT profile; returned in its stored form.</summary>
	private static string ReadCatalogProfile(AppSettingsModel settings, string value)
	{
		var id = value.Trim();
		if (id.Length == 0)
			return id;

		if (string.Equals(id, Constants.Codex.Profiles.DefaultProfileId, StringComparison.OrdinalIgnoreCase))
			return Constants.Codex.Profiles.DefaultProfileId;

		return settings.CodexProfiles.FirstOrDefault(c => string.Equals(c.ProfileId, id, StringComparison.OrdinalIgnoreCase))?.ProfileId
			?? throw new TdcpInvalidParamsException($"'{nameof(GeneralSettingsPatch.CodexCatalogProfileId)}' is not a ChatGPT profile: '{Truncate(value)}'");
	}

	/// <summary>FR.33.14: the edited table; each match is trimmed, 1-64 characters and unique (any case); each tier 0-4.</summary>
	private static System.Collections.Generic.List<CodexModelTierModel> ReadTiers(System.Collections.Generic.IReadOnlyList<CodexModelTierDto> rows)
	{
		var name = nameof(GeneralSettingsPatch.CodexModelTiers);
		SettingsPatchReader.LimitedList(name, rows);
		var result = new System.Collections.Generic.List<CodexModelTierModel>();
		foreach (var row in rows)
		{
			var match = (row?.Match ?? string.Empty).Trim();
			if (match.Length == 0 || match.Length > Constants.SettingsApi.MaxTierMatchLength)
				throw new TdcpInvalidParamsException($"'{name}' holds a row whose match is empty or longer than {Constants.SettingsApi.MaxTierMatchLength} characters");
			if (result.Any(r => string.Equals(r.Match, match, StringComparison.OrdinalIgnoreCase)))
				throw new TdcpInvalidParamsException($"'{name}' holds the match '{Truncate(match)}' twice");

			SettingsPatchReader.InRange(name, row!.Tier, Constants.SettingsApi.MinModelTier, Constants.SettingsApi.MaxModelTier);
			result.Add(new CodexModelTierModel { Match = match, Tier = row.Tier });
		}

		return result;
	}

	private static System.Collections.Generic.List<string> ReadHosts(System.Collections.Generic.IReadOnlyList<string> hosts)
	{
		SettingsPatchReader.LimitedList(nameof(GeneralSettingsPatch.MediaPreviewHosts), hosts);
		var result = new System.Collections.Generic.List<string>();
		foreach (var raw in hosts)
		{
			var host = (raw ?? string.Empty).Trim().ToLowerInvariant();
			if (host.Length == 0 || host.Length > Constants.SettingsApi.MaxHostLength || !HostPattern().IsMatch(host) || IsTooBroadWildcard(host))
				throw new TdcpInvalidParamsException($"'{nameof(GeneralSettingsPatch.MediaPreviewHosts)}' holds an invalid host: '{Truncate(raw)}'");

			if (!result.Contains(host))
				result.Add(host);
		}

		return result;
	}

	/// <summary>A wildcard needs at least two labels after <c>*.</c>: <c>*.com</c> would be "any host" (decision U5).</summary>
	private static bool IsTooBroadWildcard(string host) => host.StartsWith("*.", StringComparison.Ordinal) && !host[2..].Contains('.');

	private static FileAccessSettingsPatch ValidateAccess(FileAccessSettingsPatch access)
	{
		if (access.Mode is { } mode)
			SettingsPatchReader.EnumValue<FileAccessModeDto, FileAccessMode>(nameof(access.Mode), mode);
		if (access.MaxRelayChunkBytes is { } chunk)
			SettingsPatchReader.InRange(nameof(access.MaxRelayChunkBytes), chunk, 1, Constants.SettingsApi.MaxRelayChunkBytes);
		foreach (var (name, value) in new[]
		         {
			         (nameof(access.MaxTextFileBytes), access.MaxTextFileBytes), (nameof(access.MaxImageFileBytes), access.MaxImageFileBytes),
			         (nameof(access.MaxPdfFileBytes), access.MaxPdfFileBytes), (nameof(access.MaxVideoFileBytes), access.MaxVideoFileBytes),
		         })
		{
			if (value is { } bytes)
				SettingsPatchReader.InRange(name, bytes, 1L, Constants.SettingsApi.MaxFileCapBytes);
		}

		return access;
	}

	private static void ApplyAccess(AppSettingsModel settings, FileAccessSettingsPatch access)
	{
		settings.FileAccess ??= new FileAccessSettingsModel();
		var target = settings.FileAccess;
		if (access.Mode is { } mode) target.Mode = SettingsPatchReader.EnumValue<FileAccessModeDto, FileAccessMode>(nameof(access.Mode), mode);
		if (access.MaxRelayChunkBytes is { } chunk) target.MaxRelayChunkBytes = chunk;
		if (access.MaxTextFileBytes is { } text) target.MaxTextFileBytes = text;
		if (access.MaxImageFileBytes is { } image) target.MaxImageFileBytes = image;
		if (access.MaxPdfFileBytes is { } pdf) target.MaxPdfFileBytes = pdf;
		if (access.MaxVideoFileBytes is { } video) target.MaxVideoFileBytes = video;
	}

	private static string Truncate(string? value) =>
		value == null ? string.Empty : value.Length <= 40 ? value : value[..40] + "...";
}
