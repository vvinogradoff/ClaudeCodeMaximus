namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>tree.changed</c>: the tree version after the structural change.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TreeChangedPayload(long Version);
