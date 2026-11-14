namespace TessynDesktop.Models.Turns;

/// <summary>What <c>ITurnSendService</c> hands back when it accepted a turn.</summary>
/// <param name="TurnId">The id of the accepted turn.</param>
/// <param name="Queued">A turn of the session was already running or waiting.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnTicketModel(string TurnId, bool Queued);
