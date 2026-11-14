using TessynDesktop.Models;

namespace TessynDesktop.Models.Turns;

/// <summary>One interactive turn to run: the clean user message (the instruction block is added by the engine) and its settings.</summary>
/// <param name="Node">The session the turn belongs to.</param>
/// <param name="Message">The user's message, trimmed, without the hidden instruction block (FR.11.2).</param>
/// <param name="Settings">Launch settings for this turn.</param>
/// <param name="IsNewBranch">The one-shot New Branch toggle of this send (FR.11.4).</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnRequest(SessionNodeModel Node, string Message, TurnSettings Settings, bool IsNewBranch = false);
