namespace TessynDesktop.Models.Remote;

/// <summary>A session <c>session.switchProfile</c> could not move completely: it is not pinned to the new profile and its switch stays queued for its next turn.</summary>
/// <param name="NodeId">The session.</param>
/// <param name="Name">Its name.</param>
/// <param name="FileCount">How many files or folders could not be moved (for example held open by a running CLI); the paths stay on the PC.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionProfileSwitchFailureDto(string NodeId, string Name, int FileCount);
