using System.Threading.Tasks;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Bidirectional bridge between Tessyn sessions and a Telegram bot (FR.21). Long-polls the Bot API
/// for inbound messages while started, routing replies inside a session's Telegram topic back into
/// that session as an ordinary turn, and lets sessions/other services push notifications out.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ITelegramBridgeService
{
	/// <summary>Starts the long-poll loop. No-op if <c>TelegramEnabled</c> is false or already started.</summary>
	void Start();

	/// <summary>Stops the long-poll loop.</summary>
	void Stop();

	/// <summary>True while the long-poll loop is started (package J3, <c>telegram.status</c>). The default says false, so a stand-in need not implement it.</summary>
	bool IsRunning => false;

	/// <summary>
	/// The last failure of the poll loop as a short sentence that never carries the bot token (the Bot API URL contains it), cleared by the next
	/// successful poll; null when there is none. The default says null.
	/// </summary>
	string? LastError => null;

	/// <summary>
	/// Ensures a Telegram forum topic exists for this session, creating one on first call (FR.21.2).
	/// No-op when the bridge is disabled or misconfigured. Never throws — failures are logged.
	/// </summary>
	Task EnsureTopicForSessionAsync(SessionNodeModel node);

	/// <summary>
	/// Sends a notification/question to the session's Telegram topic (creating it if needed).
	/// Returns false (never throws) if the bridge is disabled, the session is unknown, or the send
	/// failed — callers that need feedback (e.g. the notify_user tool) can report that to the caller.
	/// </summary>
	Task<bool> NotifyAsync(string nodeId, string message);
}
