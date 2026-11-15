using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The daemon side of <c>session.setOptions</c> and <c>session.resetConversation</c> (P1.J1): the per-session instruction options (FR.11)
/// including the FR.11.12 mid-run corrections, and the detach of the Claude session id (FR.11.8).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionOptionsService
{
	/// <summary>
	/// Applies <paramref name="patch"/> to the session and saves. When the session is running and Auto-Commit or Auto-Document changed, the
	/// FR.11.12 correction is sent through <see cref="ITurnEngine.SendCorrection"/> and its notice is part of the result. Throws
	/// <see cref="TdcpException"/> for an unknown session.
	/// </summary>
	Task<SessionSetOptionsResult> SetAsync(string nodeId, SessionOptionsPatch patch);

	/// <summary>
	/// Detaches the session's Claude session id (FR.11.8) so the next prompt starts from the transcript, and stops a live streaming process.
	/// Refused with <see cref="TdcpException"/> while a turn runs.
	/// </summary>
	SessionResetConversationResult ResetConversation(string nodeId);
}
