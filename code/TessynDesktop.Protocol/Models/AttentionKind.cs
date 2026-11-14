namespace TessynDesktop.Models;

/// <summary>
/// Kind of an Attention Item (FR.23.1). Declaration order is severity order, most severe first —
/// the ordering helper and the badge's "top kind" both rely on it.
/// </summary>
/// <remarks>Created by Claude</remarks>
public enum AttentionKind
{
	Critical,
	Ask,
	Todo,
	Alert,
	Decided,
}
