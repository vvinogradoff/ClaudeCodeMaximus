using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Values of the <c>telegram</c> settings section. The bot token and the topic maps are not part of it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TelegramSettingsDto(
	bool TelegramEnabled,
	long TelegramChatId,
	IReadOnlyList<long> TelegramAllowedUserIds,
	string TelegramMentionHandle,
	TelegramMirrorModeDto TelegramMirrorMode);
