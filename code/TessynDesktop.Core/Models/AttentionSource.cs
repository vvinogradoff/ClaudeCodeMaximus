namespace TessynDesktop.Models;

/// <summary>Where an Attention Item came from (FR.23.2): <c>post_attention</c>, a captured <c>notify_user</c> (FR.23.7) or an elevated run request (FR.23.31).</summary>
/// <remarks>Created by Claude</remarks>
public enum AttentionSource
{
	Tool,
	NotifyUser,

	/// <summary>The card of an elevated run request (FR.23.31).</summary>
	ElevatedRun,
}
