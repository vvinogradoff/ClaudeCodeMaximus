namespace TessynDesktop.Models;

/// <summary>Where an Attention Item came from (FR.23.2): <c>post_attention</c> or a captured <c>notify_user</c> (FR.23.7).</summary>
/// <remarks>Created by Claude</remarks>
public enum AttentionSource
{
	Tool,
	NotifyUser,
}
