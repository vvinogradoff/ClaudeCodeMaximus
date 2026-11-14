namespace TessynDesktop.Models.Remote;

/// <summary>A session as a tree node: its name, direct Worker Session count and live state (contract 3.2).</summary>
/// <param name="Id">The session's NodeId.</param>
/// <param name="Name">Display name.</param>
/// <param name="WorkerCount">Number of Worker Sessions directly under this session.</param>
/// <param name="SupervisorId">NodeId of the supervisor; set on workers returned by <c>tree.workers</c>, omitted on top-level sessions.</param>
/// <param name="State">Live state.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TreeSessionDto(string Id, string Name, int WorkerCount, string? SupervisorId, SessionStateDto State);
