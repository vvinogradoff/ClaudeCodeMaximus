using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClaudeMaximus.Models;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeModelService : IClaudeModelService
{
    private static readonly ILogger _log = Log.ForContext<ClaudeModelService>();

    // Curated static catalog — Anthropic models with published pricing (FR.18.7).
    // Update when Anthropic releases new models or changes pricing.
    private static readonly IReadOnlyList<ClaudeModelInfo> CuratedAnthropicModels =
    [
        new("claude-fable-5",            "fable",   "Fable 5",    InputPricePerMillion: 10m, OutputPricePerMillion: 50m),
        new("claude-opus-4-8",           "opus",    "Opus 4.8",   InputPricePerMillion:  5m, OutputPricePerMillion: 25m),
        new("claude-opus-4-7",           "",        "Opus 4.7",   InputPricePerMillion:  5m, OutputPricePerMillion: 25m),
        new("claude-opus-4-6",           "",        "Opus 4.6",   InputPricePerMillion:  5m, OutputPricePerMillion: 25m),
        new("claude-sonnet-5",           "sonnet",  "Sonnet 5",   InputPricePerMillion:  2m, OutputPricePerMillion: 10m), // introductory price through 2026-08-31
        new("claude-sonnet-4-6",         "",        "Sonnet 4.6", InputPricePerMillion:  3m, OutputPricePerMillion: 15m),
        new("claude-haiku-4-5-20251001", "haiku",   "Haiku 4.5",  InputPricePerMillion:  1m, OutputPricePerMillion:  5m),
    ];

    private readonly IOllamaModelService _ollamaService;
    private readonly IAppSettingsService _appSettings;
    private IReadOnlyList<ClaudeModelInfo> _cachedModels;
    private Task? _fetchTask;

    public event EventHandler? ModelsUpdated;

    public ClaudeModelService(IOllamaModelService ollamaService, IAppSettingsService appSettings)
    {
        _ollamaService = ollamaService;
        _appSettings   = appSettings;

        // Anthropic models are immediately available from the curated catalog.
        // Ollama models are appended once EnsureModelsLoadedAsync() is called.
        _cachedModels = CuratedAnthropicModels;
    }

    public IReadOnlyList<ClaudeModelInfo> GetCachedModels() => _cachedModels;

    public Task EnsureModelsLoadedAsync()
    {
        lock (this)
        {
            _fetchTask ??= DiscoverOllamaAndMergeAsync();
        }
        return _fetchTask;
    }

    private async Task DiscoverOllamaAndMergeAsync()
    {
        var ollamaBaseUrl = _appSettings.Settings.OllamaBaseUrl;
        var ollamaModels  = await _ollamaService.GetModelsAsync(ollamaBaseUrl);

        if (ollamaModels.Count > 0)
            _log.Information("Discovered {Count} Ollama models", ollamaModels.Count);

        _cachedModels = CuratedAnthropicModels.Concat(ollamaModels).ToList();
        Dispatcher.UIThread.Post(() => ModelsUpdated?.Invoke(this, EventArgs.Empty));
    }
}
