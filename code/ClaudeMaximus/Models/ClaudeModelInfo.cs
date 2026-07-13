namespace ClaudeMaximus.Models;

/// <summary>Represents an AI model available for selection in the command bar (FR.12.3, FR.18.7).</summary>
public sealed record ClaudeModelInfo(
    /// <summary>Full model ID passed to --model, e.g. "claude-opus-4-7" or "gemma4:26b".</summary>
    string Id,
    /// <summary>Short CLI alias passed to --model when available, e.g. "opus". Empty if no alias.</summary>
    string Alias,
    /// <summary>Human-readable display name, e.g. "Opus 4.7". Not shown in dropdown (FR.12.3 uses true IDs).</summary>
    string DisplayName,
    /// <summary>Whether this model is served by Anthropic or a local Ollama instance (FR.12.14).</summary>
    ModelProvider Provider = ModelProvider.Anthropic,
    /// <summary>Whether this model supports tool/function calling. Anthropic models always do; Ollama models are probed via /api/show.</summary>
    bool SupportsTools = true,
    /// <summary>Published input price in USD per 1 million tokens (FR.18.7). 0 for Ollama/unknown models.</summary>
    decimal InputPricePerMillion = 0m,
    /// <summary>Published output price in USD per 1 million tokens (FR.18.7). 0 for Ollama/unknown models.</summary>
    decimal OutputPricePerMillion = 0m);
