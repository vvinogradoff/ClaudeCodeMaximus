namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>tree.deleteNode</c>.</summary>
/// <param name="Deleted">The node was removed.</param>
/// <param name="BlockedBy">Why it was not removed: one of <see cref="RemoteProtocol.TreeMutation.BlockedBy"/>; omitted when deleted.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeDeleteResult(bool Deleted, string? BlockedBy);
