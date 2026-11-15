using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The <c>telegram</c> section: the toggle, chat id, allowed user ids, mention handle and mirror mode. The bot token and the topic maps are not
/// part of it. The whitelist is the member list of <see cref="TelegramSettingsPatch"/>. The bridge reads the values live; enabling it starts it
/// with the daemon, as the Avalonia Settings window does.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TelegramSettingsSection : ISettingsSection
{
	[GeneratedRegex(@"^@[A-Za-z0-9_]+$")]
	private static partial Regex HandlePattern();

	/// <inheritdoc />
	public string Name => RemoteProtocol.SettingsApi.TelegramSection;

	/// <inheritdoc />
	public object Read(AppSettingsModel settings) =>
		new TelegramSettingsDto(
			settings.TelegramEnabled,
			settings.TelegramChatId,
			[.. settings.TelegramAllowedUserIds],
			settings.TelegramMentionHandle,
			(TelegramMirrorModeDto)(int)settings.TelegramMirrorMode);

	/// <inheritdoc />
	public Action CaptureRestore(AppSettingsModel settings)
	{
		var (enabled, chat, users, handle, mode) = (settings.TelegramEnabled, settings.TelegramChatId, settings.TelegramAllowedUserIds, settings.TelegramMentionHandle, settings.TelegramMirrorMode);
		return () => (settings.TelegramEnabled, settings.TelegramChatId, settings.TelegramAllowedUserIds, settings.TelegramMentionHandle, settings.TelegramMirrorMode) = (enabled, chat, users, handle, mode);
	}

	/// <inheritdoc />
	public void Apply(AppSettingsModel settings, JsonNode patch)
	{
		var p = SettingsPatchReader.Read<TelegramSettingsPatch>(patch);

		var users  = p.TelegramAllowedUserIds == null ? null : ReadUsers(p.TelegramAllowedUserIds);
		var handle = p.TelegramMentionHandle == null ? null : ReadHandle(p.TelegramMentionHandle);
		var mirror = p.TelegramMirrorMode is { } mode
			? SettingsPatchReader.EnumValue<TelegramMirrorModeDto, TelegramMirrorMode>(nameof(p.TelegramMirrorMode), mode)
			: (TelegramMirrorMode?)null;

		if (p.TelegramEnabled is { } enabled) settings.TelegramEnabled = enabled;
		if (p.TelegramChatId is { } chat)     settings.TelegramChatId = chat;
		if (users != null)                    settings.TelegramAllowedUserIds = users;
		if (handle != null)                   settings.TelegramMentionHandle = handle;
		if (mirror is { } value)              settings.TelegramMirrorMode = value;
	}

	private static List<long> ReadUsers(IReadOnlyList<long> ids)
	{
		SettingsPatchReader.LimitedList(nameof(TelegramSettingsPatch.TelegramAllowedUserIds), ids);
		if (ids.Any(id => id <= 0))
			throw new TdcpInvalidParamsException($"'{nameof(TelegramSettingsPatch.TelegramAllowedUserIds)}' holds only positive user ids");

		return [.. ids.Distinct()];
	}

	/// <summary>Empty (no prefix) or "@name" (FR.21.10).</summary>
	private static string ReadHandle(string value)
	{
		var handle = SettingsPatchReader.OptionalText(nameof(TelegramSettingsPatch.TelegramMentionHandle), value);
		if (handle.Length == 0)
			return handle;

		if (handle.Length > Constants.SettingsApi.MaxMentionHandleLength || !HandlePattern().IsMatch(handle))
			throw new TdcpInvalidParamsException($"'{nameof(TelegramSettingsPatch.TelegramMentionHandle)}' must be empty or look like @name");

		return handle;
	}
}
