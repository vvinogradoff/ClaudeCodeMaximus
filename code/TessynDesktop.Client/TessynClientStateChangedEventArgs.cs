namespace TessynDesktop.Client;

/// <summary>Raised when <see cref="ITessynClient.State"/> changes.</summary>
/// <param name="Previous">The state before the change.</param>
/// <param name="Current">The state after the change.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TessynClientStateChangedEventArgs(TessynClientState Previous, TessynClientState Current);
