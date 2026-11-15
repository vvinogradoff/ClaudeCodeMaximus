namespace TessynDesktop.Models;

/// <summary>The result of importing one Claude session: the new tree node, or why there is none.</summary>
/// <param name="Node">The session added to the tree; null when <paramref name="Failure"/> is not <see cref="ImportFailureKind.None"/>.</param>
/// <param name="Failure">Why nothing was added.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ImportOutcomeModel(SessionNodeModel? Node, ImportFailureKind Failure);
