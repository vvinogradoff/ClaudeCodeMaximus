namespace TessynDesktop.Models;

/// <summary>The git "origin" URL resolved for a directory path (null when not in a repo or no origin).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DirectoryGitOriginModel(string Path, string? OriginUrl);
