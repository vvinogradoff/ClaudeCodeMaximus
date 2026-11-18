namespace TessynDesktop.Models;

/// <summary>Where <see cref="Services.ISessionRegistry.CreateTopLevelSession"/> puts a new session (FR.15.15).</summary>
/// <param name="AfterNodeId">A session's NodeId: the new session goes right after it (a worker stands for its top-level supervisor). Wins over the other two.</param>
/// <param name="WorkingDirectory">Without an anchor: a path of a registered directory or below one; the new session goes last in that directory.</param>
/// <param name="GroupName">Without an anchor: a group directly in that directory (matched case-insensitively, created when missing); the new session goes last in it.</param>
/// <remarks>Created by Claude</remarks>
public sealed record NewSessionPlacementModel(string? AfterNodeId, string? WorkingDirectory, string? GroupName);
