namespace TessynDesktop.Codex.Events;

/// <summary><c>turn.tool</c> finished, with its one-line summary (a failed command shows its exit code).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexToolFinishedEvent(string ItemId, CodexToolKind Kind, string Summary, bool Succeeded) : CodexTurnEvent;
