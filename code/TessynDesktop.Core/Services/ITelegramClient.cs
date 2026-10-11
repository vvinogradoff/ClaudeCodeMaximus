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

	/// <summary>Renames a forum topic (FR.21.18). Returns false when Telegram answers TOPIC_NOT_MODIFIED; any other failure throws <see cref="TelegramApiException"/>.</summary>
	Task<bool> EditForumTopicAsync(long chatId, int messageThreadId, string name, CancellationToken ct = default);

	/// <summary>Long-polls for updates since <paramref name="offset"/> (exclusive), waiting up to <paramref name="timeoutSeconds"/>.</summary>
	/// <remarks>
	/// Returns one <see cref="TelegramUpdate"/> per raw update, including non-actionable markers
	/// (<see cref="TelegramUpdate.IsActionable"/> false) so callers acknowledge everything (FR.21.12).
	/// </remarks>
	Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct = default);

	/// <summary>Bot API <c>getFile</c>. Throws <see cref="TelegramFileTooBigException"/> for files over the 20 MB limit (FR.21.14).</summary>
	Task<TelegramFileInfo> GetFileAsync(string fileId, CancellationToken ct = default);

	/// <summary>Downloads <paramref name="filePath"/> (from <see cref="GetFileAsync"/>) to <paramref name="destinationPath"/> (FR.21.14).</summary>
	Task DownloadFileAsync(string filePath, string destinationPath, CancellationToken ct = default);
}
