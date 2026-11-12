using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// A session's last prompt, last response and draft state, cheap enough to ask for hundreds of sessions in one call:
/// results are cached per session file and revalidated by (length, last write time).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionActivityService
{
	/// <summary>Activity of <paramref name="node"/>; all-null with <c>HasDraft = false</c> for a session without file or draft. Thread-safe.</summary>
	SessionActivityModel GetActivity(SessionNodeModel node);
}
