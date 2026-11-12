namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>tree.createSession</c> (contract 3.5).</summary>
/// <param name="Session">The new session.</param>
/// <param name="DirectoryId">The Directory node that contains it.</param>
/// <param name="GroupId">The Group node that holds it directly; omitted when it sits directly under the directory.</param>
/// <remarks>Created by Claude</remarks>
public sealed record CreateSessionResult(TreeSessionDto Session, string DirectoryId, string? GroupId);
