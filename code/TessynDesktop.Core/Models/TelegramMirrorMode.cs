namespace TessynDesktop.Models;

/// <summary>What the Telegram Universal I/O Mirror posts to a session's topic (FR.21.22).</summary>
/// <remarks>Created by Claude</remarks>
public enum TelegramMirrorMode
{
	/// <summary>Every entry, as FR.21.9 specifies (Mailbox Deliveries shortened to one line, FR.21.23).</summary>
	Full,

	/// <summary>Only the final assistant entry of each turn, plus the turn's error text if any (default).</summary>
	AnswersOnly,

	/// <summary>Nothing is mirrored; notify_user, inbound routing and commands keep working.</summary>
	Off,
}
