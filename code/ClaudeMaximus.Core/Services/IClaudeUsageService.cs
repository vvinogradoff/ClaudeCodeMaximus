using System;
using ClaudeMaximus.Models;

namespace ClaudeMaximus.Services;

/// <summary>
/// Fetches and caches Anthropic plan utilisation data for the active profile (FR.18.4–18.6).
/// Polls the OAuth usage endpoint every 5 minutes. On HTTP 401 the fetch stops and
/// previously cached data (if any) is retained.
/// </summary>
public interface IClaudeUsageService
{
    /// <summary>
    /// The most recently successfully fetched usage snapshot, or null if no successful
    /// fetch has completed for the current session.
    /// </summary>
    ClaudeUsageData? CachedUsage { get; }

    /// <summary>
    /// Raised on the UI thread when <see cref="CachedUsage"/> has been updated with
    /// a fresh fetch from the API.
    /// </summary>
    event EventHandler? UsageUpdated;

    /// <summary>
    /// Switches polling to the supplied profile credentials file and triggers an immediate
    /// refresh. Pass <c>null</c> to stop polling (e.g. when no session is selected).
    /// </summary>
    void SetActiveProfile(string? credentialsFilePath);
}
