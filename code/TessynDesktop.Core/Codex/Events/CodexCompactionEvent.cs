namespace TessynDesktop.Codex.Events;

/// <summary>A completed <c>contextCompaction</c> item: a COMPACTION separator (FR.3.4, FR.33.12).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexCompactionEvent(string ItemId) : CodexTurnEvent;
