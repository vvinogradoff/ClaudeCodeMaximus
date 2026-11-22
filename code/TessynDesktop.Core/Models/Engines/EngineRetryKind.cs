namespace TessynDesktop.Models.Engines;

/// <summary>Why an engine retries a turn by itself.</summary>
/// <remarks>Created by Claude</remarks>
public enum EngineRetryKind
{
	/// <summary>A transient credential failure (Claude: the OAuth refresh race, ISSUE.010). The same prompt is sent again after a short delay.</summary>
	TransientAuth,

	/// <summary>The engine no longer knows the conversation (Claude: "No conversation found"). A new one is seeded from the transcript (FR.11.10).</summary>
	LostConversation,
}
