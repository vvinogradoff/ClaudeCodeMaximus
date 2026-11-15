namespace TessynDesktop.Models.Turns;

/// <summary>
/// The turn ended; sent before its <c>turn.completed</c>, so a sink can flush what it still holds.
/// </summary>
/// <param name="Ran">
/// The turn held the session's turn lock, so the file service reports <c>TurnCompleted</c> for it. False for a turn that never ran (stopped
/// while queued, or failed before its dispatch): nothing else will report its end, so the sink publishes the <c>turn.completed</c> itself.
/// </param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnFinishedProgress(string NodeId, string TurnId, bool Cancelled, string? Error, bool Ran) : TurnProgress(NodeId, TurnId);
