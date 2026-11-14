using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>A Directory node with its groups and top-level sessions, both in model order (contract 3.2).</summary>
/// <param name="Id">The directory's NodeId.</param>
/// <param name="Label">Display label (the directory label service); never the filesystem path.</param>
/// <param name="IsExpanded">The desktop's persisted expansion flag; the web uses it only as its initial state.</param>
/// <param name="Groups">Child groups.</param>
/// <param name="Sessions">Top-level sessions directly under the directory.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeDirectoryDto(string Id, string Label, bool IsExpanded, IReadOnlyList<TreeGroupDto> Groups, IReadOnlyList<TreeSessionDto> Sessions);
