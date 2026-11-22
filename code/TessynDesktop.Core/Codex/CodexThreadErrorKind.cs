namespace TessynDesktop.Codex;

/// <summary>What a <c>-32600</c> answer to <c>thread/resume</c> or <c>turn/start</c> means (FR.33.7).</summary>
/// <remarks>Created by Claude</remarks>
public enum CodexThreadErrorKind
{
	/// <summary>Not one of the known thread failures.</summary>
	Unknown,

	/// <summary><c>no rollout found for thread id …</c> or <c>invalid session id: …</c>: detach the id and start a new, reseeded thread.</summary>
	ThreadGone,

	/// <summary><c>… already has an active writer</c>: another Codex process on the same home has the thread open; keep the id.</summary>
	ActiveWriter,

	/// <summary><c>thread not found: …</c> on <c>turn/start</c>: the thread is not loaded on this host; resume it once and retry.</summary>
	NotLoaded,
}
