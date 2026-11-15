namespace TessynDesktop.Models.Turns;

/// <summary>How an engine turn ended; an unexpected failure is thrown instead, after the turn was reported complete.</summary>
/// <param name="TurnId">The id the turn carried on the wire.</param>
/// <param name="Cancelled">The turn was stopped.</param>
/// <param name="ErrorText">The text of the error result, when the turn ended in one.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnOutcome(string TurnId, bool Cancelled, string? ErrorText);
