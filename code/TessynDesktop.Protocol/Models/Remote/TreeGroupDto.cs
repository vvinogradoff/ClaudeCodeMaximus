using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>A Group node with its child groups and top-level sessions, both in model order (contract 3.2).</summary>
/// <param name="Id">The group's NodeId.</param>
/// <param name="Name">Group name.</param>
/// <param name="IsExpanded">The desktop's persisted expansion flag; the web uses it only as its initial state.</param>
/// <param name="Groups">Child groups.</param>
/// <param name="Sessions">Top-level sessions directly in this group.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeGroupDto(string Id, string Name, bool IsExpanded, IReadOnlyList<TreeGroupDto> Groups, IReadOnlyList<TreeSessionDto> Sessions);
