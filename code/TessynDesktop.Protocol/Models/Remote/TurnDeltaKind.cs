namespace TessynDesktop.Models.Remote;

/// <summary>What a <c>turn.delta</c> carries: assistant text or the model's thinking.</summary>
/// <remarks>Created by Claude</remarks>
public enum TurnDeltaKind
{
	Text,
	Thinking,
}
