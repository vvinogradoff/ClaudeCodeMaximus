namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>turn.status</c>: the turn moved to a new state.</summary>
/// <param name="NodeId">The session the turn belongs to.</param>
/// <param name="TurnId">The turn.</param>
/// <param name="State">The new state.</param>
/// <param name="Detail">Why, for a retry: <c>context</c> (no conversation found) or <c>auth</c> (transient OAuth refresh race).</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnStatusPayload(string NodeId, string TurnId, TurnState State, string? Detail = null);
