namespace TessynDesktop.Models.Remote;

/// <summary>Params of <c>turn.usage</c>: the token counts and cost claude reported at the end of a response.</summary>
/// <param name="NodeId">The session the turn belongs to.</param>
/// <param name="TurnId">The turn.</param>
/// <param name="InputTokens">Input tokens of the result event (0 when not reported).</param>
/// <param name="OutputTokens">Output tokens of the result event (0 when not reported).</param>
/// <param name="CostUsd">Total cost in USD of the result event (0 when not reported).</param>
/// <remarks>Created by Claude</remarks>
public sealed record TurnUsagePayload(string NodeId, string TurnId, int InputTokens, int OutputTokens, double CostUsd);
