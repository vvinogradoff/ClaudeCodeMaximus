using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Patch of the <c>telegram</c> section (see <see cref="GeneralSettingsPatch"/> for the rules).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TelegramSettingsPatch(
	bool? TelegramEnabled = null,
	long? TelegramChatId = null,
	IReadOnlyList<long>? TelegramAllowedUserIds = null,
	string? TelegramMentionHandle = null,
	TelegramMirrorModeDto? TelegramMirrorMode = null);
