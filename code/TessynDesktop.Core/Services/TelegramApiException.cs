using System;

namespace TessynDesktop.Services;

/// <summary>
/// Thrown by <see cref="ITelegramClient.EditForumTopicAsync"/> when the Bot API rejects the call,
/// carrying the structured error so callers can react to rate limits and rights problems (FR.21.18).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TelegramApiException : Exception
{
	/// <summary>Telegram's <c>error_code</c>, else the HTTP status code.</summary>
	public int ErrorCode { get; }

	/// <summary>Telegram's <c>description</c>, else the raw response body.</summary>
	public string Description { get; }

	/// <summary>Seconds to wait before retrying (<c>parameters.retry_after</c>), when Telegram supplies it.</summary>
	public int? RetryAfterSeconds { get; }

	public TelegramApiException(int errorCode, string description, int? retryAfterSeconds = null)
		: base($"Telegram API call failed ({errorCode}): {description}")
	{
		ErrorCode = errorCode;
		Description = description;
		RetryAfterSeconds = retryAfterSeconds;
	}
}
