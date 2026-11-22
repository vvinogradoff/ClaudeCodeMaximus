namespace TessynDesktop.Codex.Protocol;

/// <summary><c>item/reasoning/summaryTextDelta</c>: streamed thinking summary.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexReasoningSummaryTextDeltaNotification
{
	public string ThreadId { get; set; } = null!;
	public string TurnId { get; set; } = null!;
	public string ItemId { get; set; } = null!;
	public string Delta { get; set; } = null!;
	public int SummaryIndex { get; set; }
}
