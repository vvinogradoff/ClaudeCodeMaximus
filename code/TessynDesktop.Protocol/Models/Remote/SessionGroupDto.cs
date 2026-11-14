using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>The sessions of one Directory node in a directory-grouped list (contract 3, shared DTOs).</summary>
/// <param name="DirectoryId">The directory's NodeId.</param>
/// <param name="DirectoryLabel">Display label.</param>
/// <param name="Sessions">Drafts first, then newest Last Activity first.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionGroupDto(string DirectoryId, string DirectoryLabel, IReadOnlyList<SessionRowDto> Sessions);
