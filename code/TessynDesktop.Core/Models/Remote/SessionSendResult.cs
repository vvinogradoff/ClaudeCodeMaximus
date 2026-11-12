namespace TessynDesktop.Models.Remote;

/// <summary>Answer of session.send: the turn was accepted and runs in the background.</summary>
/// <param name="Accepted">Always true; a refused send is a JSON-RPC error.</param>
/// <param name="Queued">A turn was already running, so this one waits for it.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionSendResult(bool Accepted, bool Queued);
