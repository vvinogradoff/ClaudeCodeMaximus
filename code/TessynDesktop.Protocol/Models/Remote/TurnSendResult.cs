namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>turn.send</c>: the turn was accepted and runs in the background.</summary>
/// <param name="TurnId">The id of the turn; matches the <c>turnId</c> of its notifications and of its <c>turn.completed</c>.</param>
/// <param name="Accepted">Always true; a refused send is a JSON-RPC error.</param>
/// <param name="Queued">A turn was already running or waiting, so this one waits for it.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnSendResult(string TurnId, bool Accepted, bool Queued);
