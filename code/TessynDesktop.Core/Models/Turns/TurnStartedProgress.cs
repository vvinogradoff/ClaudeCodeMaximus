namespace TessynDesktop.Models.Turns;

/// <summary>A turn was accepted.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TurnStartedProgress(string NodeId, string TurnId, string? ClientRequestId) : TurnProgress(NodeId, TurnId);
