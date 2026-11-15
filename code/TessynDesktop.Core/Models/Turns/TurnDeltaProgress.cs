using TessynDesktop.Models.Remote;

namespace TessynDesktop.Models.Turns;

/// <summary>The turn produced assistant text or thinking.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TurnDeltaProgress(string NodeId, string TurnId, TurnDeltaKind Kind, string Text) : TurnProgress(NodeId, TurnId);
