using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeModelService : IClaudeModelService
{
    private static readonly ILogger _log = Log.ForContext<ClaudeModelService>();

    // Curated static catalog — Anthropic models with published pricing (FR.18.7).
    // Update when Anthropic releases new models or changes pricing.
    private static readonly IReadOnlyList<ClaudeModelInfo> CuratedAnthropicModels =
    [
        new("claude-fable-5-2",          "fable",   "Fable 5.2",  InputPricePerMillion:  8m,    OutputPricePerMillion: 40m),
        new("claude-opus-5-5",           "opus",    "Opus 5.5",   InputPricePerMillion:  4m,    OutputPricePerMillion: 20m),
        new("claude-sonnet-5-5",         "sonnet",  "Sonnet 5.5", InputPricePerMillion:  2m,    OutputPricePerMillion: 10m),
        // Haiku 5.5 is priced by prompt length: $0.10/$0.50 up to 100K input tokens, $0.50/$2.50 above. The catalog keeps the base tier.
        new(Constants.ClaudeAssist.PreferredModelId, "haiku", "Haiku 5.5", InputPricePerMillion: 0.10m, OutputPricePerMillion: 0.50m),
    ];

    private readonly IOllamaModelService _ollamaService;
    private readonly IAppSettingsService _appSettings;
    private readonly IUiDispatcher _uiDispatcher;
    private IReadOnlyList<ClaudeModelInfo> _cachedModels;
    private Task? _fetchTask;

    public event EventHandler? ModelsUpdated;

    public ClaudeModelService(IOllamaModelService ollamaService, IAppSettingsService appSettings, IUiDispatcher uiDispatcher)
    {
        _ollamaService = ollamaService;
        _appSettings   = appSettings;
        _uiDispatcher  = uiDispatcher;

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

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // Not the once-per-launch task: ask again now, so a model pulled since the launch, or a changed Ollama URL, shows up.
        // The once-per-launch task is only satisfied by a refresh that worked: a cancelled or faulted one must not become the task that every later
        // EnsureModelsLoadedAsync returns.
        await DiscoverOllamaAndMergeAsync(cancellationToken);
        lock (this)
        {
            _fetchTask = Task.CompletedTask;
        }
    }

    private async Task DiscoverOllamaAndMergeAsync(CancellationToken cancellationToken = default)
    {
        string ollamaBaseUrl;
        lock (_appSettings.SyncRoot)
            ollamaBaseUrl = _appSettings.Settings.OllamaBaseUrl;

        var ollamaModels  = await _ollamaService.GetModelsAsync(ollamaBaseUrl, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested(); // the lookup swallows a cancel and answers "none": do not replace the list with that

        if (ollamaModels.Count > 0)
            _log.Information("Discovered {Count} Ollama models", ollamaModels.Count);

        _cachedModels = CuratedAnthropicModels.Concat(ollamaModels).ToList();
        _uiDispatcher.Post(() => ModelsUpdated?.Invoke(this, EventArgs.Empty));
    }
}
