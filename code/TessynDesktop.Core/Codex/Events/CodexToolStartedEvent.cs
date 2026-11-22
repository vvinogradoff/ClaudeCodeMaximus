namespace TessynDesktop.Codex.Events;

/// <summary><c>turn.tool</c> started, with its one-line summary (for example <c>$ git status</c>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexToolStartedEvent(string ItemId, CodexToolKind Kind, string Summary) : CodexTurnEvent;
