namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>turn.started</c>: a turn was accepted (it may still be queued, see <c>turn.status</c>).</summary>
/// <param name="NodeId">The session the turn belongs to.</param>
/// <param name="TurnId">The id every later event of the turn carries.</param>
/// <param name="ClientRequestId">The <c>clientRequestId</c> of the <c>turn.send</c> that started it, when it came with one.</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnStartedPayload(string NodeId, string TurnId, string? ClientRequestId = null);
