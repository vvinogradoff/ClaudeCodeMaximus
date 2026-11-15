namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>tree.renameNode</c>.</summary>
/// <param name="Name">The stored (trimmed) name.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeRenameResult(string Name);
