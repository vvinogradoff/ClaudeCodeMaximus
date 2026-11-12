namespace TessynDesktop.Models.Remote;

/// <summary>Attention badge of a session row: every open item (Decided included) and the most severe kind among them (FR.23.17).</summary>
/// <param name="Count">Number of open items; always at least 1 — a row without open items carries no badge at all.</param>
/// <param name="TopKind">Most severe open kind; serialized as the camelCase wire name ("critical", "ask", "todo", "alert", "decided").</param>
/// <remarks>Created by Claude</remarks>
public sealed record RowAttentionDto(int Count, AttentionKind TopKind);
