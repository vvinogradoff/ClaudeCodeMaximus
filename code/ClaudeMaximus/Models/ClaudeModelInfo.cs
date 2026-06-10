namespace ClaudeMaximus.Models;

/// <summary>Represents a Claude AI model available for selection in the command bar (FR.12.3).</summary>
public sealed record ClaudeModelInfo(
    /// <summary>Full model ID passed to --model when no alias is available, e.g. "claude-opus-4-7".</summary>
    string Id,
    /// <summary>Short CLI alias passed to --model when available, e.g. "opus". Empty if model has no alias.</summary>
    string Alias,
    /// <summary>Human-readable display name with version shown in the UI, e.g. "Opus 4.7".</summary>
    string DisplayName);
