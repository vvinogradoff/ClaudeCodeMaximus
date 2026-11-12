namespace TessynDesktop.Models.Remote;

/// <summary>Answer of session.stop.</summary>
/// <param name="Stopped">A live process was stopped or a running turn was cancelled.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionStopResult(bool Stopped);
