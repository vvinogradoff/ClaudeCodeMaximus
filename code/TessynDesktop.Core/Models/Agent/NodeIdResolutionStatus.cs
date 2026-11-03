namespace TessynDesktop.Models.Agent;

/// <summary>Outcome of resolving a full NodeId or prefix to one session (FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
public enum NodeIdResolutionStatus
{
	Resolved,
	NotFound,
	TooShort,
	Ambiguous,
}
