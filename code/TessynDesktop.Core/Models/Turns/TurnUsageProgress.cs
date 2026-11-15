namespace TessynDesktop.Models.Turns;

/// <summary>Claude reported the usage of a response.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TurnUsageProgress(string NodeId, string TurnId, int InputTokens, int OutputTokens, double CostUsd) : TurnProgress(NodeId, TurnId);
