using TessynDesktop.Models.Remote;

namespace TessynDesktop.Models.Turns;

/// <summary>The turn moved to another state (queued, running, retrying).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TurnStatusProgress(string NodeId, string TurnId, TurnState State, string? Detail) : TurnProgress(NodeId, TurnId);
