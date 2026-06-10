using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// Provides the list of available Claude models, fetching it dynamically from the CLI
/// and caching the result for 24 hours.
/// </summary>
public interface IClaudeModelService
{
    /// <summary>Returns the in-memory cached model list. Always instant; never null.</summary>
    IReadOnlyList<ClaudeModelInfo> GetCachedModels();

    /// <summary>
    /// Ensures models are loaded from the Claude CLI. Only performs a live fetch once per 24 hours.
    /// Raises <see cref="ModelsUpdated"/> on the UI thread when the list changes.
    /// Safe to call multiple times — subsequent calls are no-ops while a fetch is in progress.
    /// </summary>
    Task EnsureModelsLoadedAsync(string claudePath);

    /// <summary>Raised on the UI thread after the model list has been refreshed from the CLI.</summary>
    event EventHandler? ModelsUpdated;
}
