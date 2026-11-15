namespace TessynDesktop.Models.Remote;

/// <summary>Result of the Telegram methods: the bridge and whether a bot token is stored. The token itself is write-only.</summary>
/// <param name="Enabled">The <c>telegramEnabled</c> setting.</param>
/// <param name="Running">The bridge is polling.</param>
/// <param name="HasToken">A bot token is stored.</param>
/// <param name="LastError">The last failure of the bridge as a short sentence, until a poll succeeds; never the token.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TelegramStatusDto(bool Enabled, bool Running, bool HasToken, string? LastError = null);
