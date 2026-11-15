namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>tree.addDirectory</c>.</summary>
/// <param name="DirectoryId">The NodeId of the directory (the existing one when the path was already in the tree).</param>
/// <param name="Label">The directory's display label.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeAddDirectoryResult(string DirectoryId, string Label);
