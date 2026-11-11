namespace TessynDesktop.Models;

/// <summary>
/// One session of the tree snapshot the Session Tag Index is built from (FR.24.13): identity, current name
/// (for name-derived tags), supervisor link (Worker Session roll-up) and the path of the containing Directory node.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionTagIndexSessionModel(string NodeId, string Name, string? SupervisorNodeId, string DirectoryPath);
