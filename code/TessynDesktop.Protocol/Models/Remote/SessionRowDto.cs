namespace TessynDesktop.Models.Remote;

/// <summary>A session row of a Tags list (contract 3, shared DTOs).</summary>
/// <param name="Id">The session's NodeId.</param>
/// <param name="Name">Display name.</param>
/// <param name="State">Live state.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionRowDto(string Id, string Name, SessionStateDto State);
