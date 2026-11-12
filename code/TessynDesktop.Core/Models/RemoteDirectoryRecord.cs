using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>Immutable copy of a Directory node and everything below it (see <see cref="RemoteSessionRecord"/>).</summary>
/// <param name="Id">The directory's NodeId.</param>
/// <param name="Path">The directory's filesystem path (never sent to clients).</param>
/// <param name="IsExpanded">The desktop's persisted expansion flag.</param>
/// <param name="Groups">Child groups in model order.</param>
/// <param name="Sessions">Sessions directly under the directory in model order (Worker Sessions included).</param>
/// <remarks>Created by Claude</remarks>
public sealed record RemoteDirectoryRecord(
	string Id,
	string Path,
	bool IsExpanded,
	IReadOnlyList<RemoteGroupRecord> Groups,
	IReadOnlyList<RemoteSessionRecord> Sessions);
