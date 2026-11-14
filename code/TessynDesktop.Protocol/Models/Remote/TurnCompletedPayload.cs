namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>turn.completed</c>.</summary>
/// <param name="NodeId">The session whose turn ended.</param>
/// <param name="Cancelled">The turn was stopped.</param>
/// <param name="Error">The turn's error text, when it ended in an error.</param>
/// <param name="TurnId">The id of the turn (additive, P1.I): set when an engine turn ended, absent for a headless turn without one.</param>
/// <param name="NeverRan">Additive, local only (P1.I): the turn never ran (stopped while queued, failed before dispatch). The relay does not carry such a completion (ISSUE.012).</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnCompletedPayload(string NodeId, bool Cancelled, string? Error, string? TurnId = null, bool? NeverRan = null);
