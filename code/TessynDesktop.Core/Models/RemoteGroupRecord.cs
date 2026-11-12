using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>Immutable copy of a Group node and everything below it (see <see cref="RemoteSessionRecord"/>).</summary>
/// <param name="Id">The group's NodeId.</param>
/// <param name="Name">Group name.</param>
/// <param name="IsExpanded">The desktop's persisted expansion flag.</param>
/// <param name="Groups">Child groups in model order.</param>
/// <param name="Sessions">Sessions directly in the group in model order (Worker Sessions included).</param>
/// <remarks>Created by Claude</remarks>
public sealed record RemoteGroupRecord(
	string Id,
	string Name,
	bool IsExpanded,
	IReadOnlyList<RemoteGroupRecord> Groups,
	IReadOnlyList<RemoteSessionRecord> Sessions);
