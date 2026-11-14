namespace TessynDesktop.Models.Remote;

/// <summary>
/// Params of <c>turn.delta</c>: a preview of what the turn is producing. The persisted entry stays the source of truth; a gap in
/// <paramref name="Seq"/> means deltas were dropped and the client should re-read the entry.
/// </summary>
/// <param name="NodeId">The session the turn belongs to.</param>
/// <param name="TurnId">The turn.</param>
/// <param name="Seq">Position in the turn's delta sequence (starts at 1); a jump means dropped deltas.</param>
/// <param name="Kind">Assistant text or thinking.</param>
/// <param name="Text">The text; consecutive deltas of one batch window are merged.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnDeltaPayload(string NodeId, string TurnId, long Seq, TurnDeltaKind Kind, string Text);
