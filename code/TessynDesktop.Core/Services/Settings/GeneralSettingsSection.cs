using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The <c>general</c> section: session files root, Claude path, source tree, proxy, Ollama URL, agent tools, remote file access and the media
/// preview hosts. The whitelist is the member list of <see cref="GeneralSettingsPatch"/> and <see cref="FileAccessSettingsPatch"/>.
/// The session files root, the Claude path and the source tree are <b>read-only</b> here (decision U1): the first is where files are written and the
/// other two name what the daemon executes, so they are edited in the Avalonia Settings window only.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class GeneralSettingsSection : ISettingsSection
{
	[GeneratedRegex(@"^(\*\.)?[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$")]
	private static partial Regex HostPattern();

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
			new FileAccessSettingsDto(
				(FileAccessModeDto)(int)access.Mode, access.MaxRelayChunkBytes, access.MaxTextFileBytes,
				access.MaxImageFileBytes, access.MaxPdfFileBytes, access.MaxVideoFileBytes),
			[.. settings.MediaPreviewHosts]);
	}

	/// <inheritdoc />
	public Action CaptureRestore(AppSettingsModel settings)
	{
		var (proxy, ollama, agentTools, hosts) = (settings.HttpsProxy, settings.OllamaBaseUrl, settings.AgentToolsEnabled, settings.MediaPreviewHosts);
		var hadAccess = settings.FileAccess;
		var access = hadAccess == null ? ((FileAccessMode, int, long, long, long, long)?)null
			: (hadAccess.Mode, hadAccess.MaxRelayChunkBytes, hadAccess.MaxTextFileBytes, hadAccess.MaxImageFileBytes, hadAccess.MaxPdfFileBytes, hadAccess.MaxVideoFileBytes);
		return () =>
		{
			(settings.HttpsProxy, settings.OllamaBaseUrl, settings.AgentToolsEnabled, settings.MediaPreviewHosts) = (proxy, ollama, agentTools, hosts);
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

		if (proxy != null)            settings.HttpsProxy = proxy;
		if (ollama != null)           settings.OllamaBaseUrl = ollama;
		if (p.AgentToolsEnabled is { } agentTools) settings.AgentToolsEnabled = agentTools;
		if (hosts != null)            settings.MediaPreviewHosts = hosts;
		if (access != null)
			ApplyAccess(settings, access);
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
