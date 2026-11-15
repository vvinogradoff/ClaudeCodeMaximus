using TessynDesktop.Models;

namespace TessynDesktop.Models.Turns;

/// <summary>One interactive turn to run: the clean user message (the instruction block is added by the engine) and its settings.</summary>
/// <param name="Node">The session the turn belongs to.</param>
/// <param name="Message">The user's message, trimmed, without the hidden instruction block (FR.11.2).</param>
/// <param name="Settings">Launch settings for this turn.</param>
/// <param name="IsNewBranch">The one-shot New Branch toggle of this send (FR.11.4).</param>
/// <param name="TurnId">The id the turn carries on the wire; the engine makes one when null (a caller that must answer with it up front passes it).</param>
/// <param name="ClientRequestId">The caller's correlation id, echoed by <c>turn.started</c>.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnRequest(SessionNodeModel Node, string Message, TurnSettings Settings, bool IsNewBranch = false, string? TurnId = null, string? ClientRequestId = null);
