using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>tree.moveTargets</c>.</summary>
/// <param name="ParentIds">The NodeIds of every directory and group the session may be moved into (its current parent included).</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeMoveTargetsResult(IReadOnlyList<string> ParentIds);
