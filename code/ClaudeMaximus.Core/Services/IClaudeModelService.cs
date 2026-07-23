using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// Provides the list of available models. Anthropic models come from a built-in curated
/// catalog (FR.18.7); Ollama models are discovered live from the local Ollama instance
/// on first call. Raises <see cref="ModelsUpdated"/> on the UI thread when the list changes.
/// </summary>
public interface IClaudeModelService
{
    /// <summary>Returns the in-memory cached model list. Always instant; never null.</summary>
    IReadOnlyList<ClaudeModelInfo> GetCachedModels();

    /// <summary>
    /// Ensures the Ollama models have been discovered and merged with the curated Anthropic list.
    /// Only performs the Ollama discovery once per app launch (subsequent calls are no-ops).
    /// Raises <see cref="ModelsUpdated"/> on the UI thread when the list changes.
    /// </summary>
    Task EnsureModelsLoadedAsync();

    /// <summary>Raised on the UI thread after the model list has been refreshed.</summary>
    event EventHandler? ModelsUpdated;
}
