namespace TessynDesktop.Models;

/// <summary>What <c>IAttentionService.ResolveBySession</c> did (FR.23.5).</summary>
/// <remarks>Created by Claude</remarks>
public enum AttentionResolveOutcome
{
	Closed,
	AlreadyClosed,
	Unknown,

	/// <summary>The item is the card of an elevated run: the session withdraws the request with <c>cancel_elevated_run</c> instead (FR.23.31).</summary>
	ElevatedRun,
}
