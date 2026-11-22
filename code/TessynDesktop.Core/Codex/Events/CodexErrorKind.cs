namespace TessynDesktop.Codex.Events;

/// <summary>How a Codex error is handled (FR.33.13). Detection is structured only: Codex text is never pattern-matched.</summary>
/// <remarks>Created by Claude</remarks>
public enum CodexErrorKind
{
	/// <summary><c>unauthorized</c>: the session's and the profile's auth-error state.</summary>
	Auth,

	/// <summary><c>usageLimitExceeded</c> or <c>rateLimitExceeded</c>: the limits-error warning.</summary>
	Limit,

	/// <summary><c>contextWindowExceeded</c>: the compact hint, no retry.</summary>
	ContextWindowExceeded,

	/// <summary>Anything else (<c>serverOverloaded</c>, <c>httpConnectionFailed</c>, <c>modelNotFound</c>, no code…).</summary>
	Other,
}
