using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>tree.nodeInfo</c>: one item per known id, in request order; unknown ids are left out.</summary>
/// <param name="Items">The nodes found.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeNodeInfoResult(IReadOnlyList<TreeNodeInfoItem> Items);
