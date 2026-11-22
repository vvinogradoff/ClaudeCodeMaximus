namespace TessynDesktop.Codex.Protocol;

/// <summary><c>item/agentMessage/delta</c>: streamed assistant text.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexAgentMessageDeltaNotification
{
	public string ThreadId { get; set; } = null!;
	public string TurnId { get; set; } = null!;
	public string ItemId { get; set; } = null!;
	public string Delta { get; set; } = null!;
}
