namespace TessynDesktop.Models.Remote;

/// <summary>Answer of session.setStreaming.</summary>
/// <param name="Enabled">Streaming Session Mode after the call.</param>
/// <param name="NeedsConfirm">Turning it off was refused because a process is alive and <c>force</c> was not given.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SetStreamingResult(bool Enabled, bool NeedsConfirm);
