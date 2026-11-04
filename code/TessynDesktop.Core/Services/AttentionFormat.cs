using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>Lower-case wire names of kinds and statuses, as used in tool results, SYSTEM entries and the Pinned list (FR.23).</summary>
/// <remarks>Created by Claude</remarks>
public static class AttentionFormat
{
	public static string KindName(AttentionKind kind) => kind.ToString().ToLowerInvariant();

	public static string StatusName(AttentionStatus status) => status.ToString().ToLowerInvariant();
}
