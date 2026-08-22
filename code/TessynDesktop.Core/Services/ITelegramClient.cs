using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Thin wrapper around the Telegram Bot HTTP API (FR.21). No Telegram SDK dependency — raw HTTP +
/// JSON, mirroring the pattern already proven in the FanvueCharacters project's Telegram bot.
/// The bot token is resolved per call from <see cref="ITelegramTokenStore"/> so a token saved
/// through Settings takes effect on the next call without restarting the bridge.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITelegramClient
{
	/// <summary>Sends a text message. <paramref name="messageThreadId"/> null = the chat's General topic.</summary>
	Task SendMessageAsync(long chatId, string text, int? messageThreadId, CancellationToken ct = default);

	/// <summary>Creates a forum topic in a Topics-enabled supergroup. Returns its message_thread_id.</summary>
	Task<int> CreateForumTopicAsync(long chatId, string name, CancellationToken ct = default);

	/// <summary>Long-polls for updates since <paramref name="offset"/> (exclusive), waiting up to <paramref name="timeoutSeconds"/>.</summary>
	Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct = default);
}
