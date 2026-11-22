namespace TessynDesktop.Codex.Events;

/// <summary>What a <see cref="CodexProgressEvent"/> line is about.</summary>
/// <remarks>Created by Claude</remarks>
public enum CodexProgressKind
{
	/// <summary><c>Plan: &lt;done&gt;/&lt;total&gt; - &lt;current step&gt;</c> from <c>turn/plan/updated</c>.</summary>
	Plan,

	/// <summary><c>Codex is retrying: &lt;message&gt;</c> from an <c>error</c> with <c>willRetry: true</c>.</summary>
	Retry,

	/// <summary><c>Codex: &lt;type&gt;</c> for an item type Tessyn does not map.</summary>
	UnmappedItem,
}
