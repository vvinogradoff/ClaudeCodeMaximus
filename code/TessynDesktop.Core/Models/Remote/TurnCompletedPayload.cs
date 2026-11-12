namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>turn.completed</c>.</summary>
/// <param name="NodeId">The session whose turn ended.</param>
/// <param name="Cancelled">The turn was stopped.</param>
/// <param name="Error">The turn's error text, when it ended in an error.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnCompletedPayload(string NodeId, bool Cancelled, string? Error);
