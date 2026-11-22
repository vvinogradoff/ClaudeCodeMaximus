namespace TessynDesktop.Codex;

/// <summary>Why a host's process was stopped on purpose (not a crash). Turns still in flight on it end with an error that says so.</summary>
/// <remarks>Created by Claude</remarks>
public enum CodexHostStopReason
{
	/// <summary><see cref="CodexAppServerHost.StopAsync"/> or dispose (daemon exit).</summary>
	Requested,

	/// <summary>"Terminate all" (FR.15.5).</summary>
	Terminated,

	/// <summary>The idle shutdown (FR.33.6); it only happens with no turn in progress.</summary>
	Idle,

	/// <summary>The ChatGPT profile was deleted (FR.33.5).</summary>
	ProfileRemoved,
}
