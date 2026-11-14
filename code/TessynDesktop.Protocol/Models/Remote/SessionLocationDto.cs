namespace TessynDesktop.Models.Remote;

/// <summary>Where a session sits in the tree: its directory's label and the readable path to it.</summary>
/// <param name="DirectoryLabel">The directory's display label.</param>
/// <param name="TreePath">Directory and group names leading to the session.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionLocationDto(string DirectoryLabel, string TreePath);
