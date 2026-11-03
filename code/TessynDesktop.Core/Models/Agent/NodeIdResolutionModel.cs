using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Agent;

/// <summary>Result of resolving a NodeId or prefix to a session (FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record NodeIdResolutionModel
{
	public required NodeIdResolutionStatus Status { get; init; }

	/// <summary>Full NodeId when <see cref="NodeIdResolutionStatus.Resolved"/>.</summary>
	public string? NodeId { get; init; }

	/// <summary>Up to <see cref="Constants.Agent.AmbiguousNodeIdMaxMatches"/> when ambiguous.</summary>
	public IReadOnlyList<SessionSnapshotModel> Matches { get; init; } = Array.Empty<SessionSnapshotModel>();

	public int MatchCount { get; init; }
}
