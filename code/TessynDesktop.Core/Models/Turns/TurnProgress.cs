namespace TessynDesktop.Models.Turns;

/// <summary>Something a running turn reports, before it is turned into wire notifications (P1.I).</summary>
/// <param name="NodeId">The session the turn belongs to.</param>
/// <param name="TurnId">The turn.</param>
/// <remarks>Created by Claude</remarks>
public abstract record TurnProgress(string NodeId, string TurnId);
