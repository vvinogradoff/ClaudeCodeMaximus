using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClaudeMaximus.Models;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeModelService : IClaudeModelService
{
    private static readonly ILogger _log = Log.ForContext<ClaudeModelService>();

    // Fallback list used when the CLI is unavailable or the cache is empty.
    // Kept up-to-date with the current production Claude model line-up.
    private static readonly IReadOnlyList<ClaudeModelInfo> FallbackModels =
    [
        new("claude-opus-4-7",           "opus",   "Opus 4.7"),
        new("claude-sonnet-4-6",         "sonnet", "Sonnet 4.6"),
        new("claude-haiku-4-5-20251001", "haiku",  "Haiku 4.5"),
    ];

    private readonly IClaudeProcessManager _processManager;
    private readonly string _cacheFilePath;
    private IReadOnlyList<ClaudeModelInfo> _cachedModels;
    private Task? _fetchTask;

    public event EventHandler? ModelsUpdated;

    public ClaudeModelService(IClaudeProcessManager processManager)
    {
        _processManager = processManager;
        _cacheFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Constants.AppDataFolderName,
            "models-cache.json");

        _cachedModels = TryLoadFromFileCache() ?? FallbackModels;
    }

    public IReadOnlyList<ClaudeModelInfo> GetCachedModels() => _cachedModels;

    public Task EnsureModelsLoadedAsync(string claudePath)
    {
        lock (this)
        {
            _fetchTask ??= FetchAndCacheAsync(claudePath);
        }
        return _fetchTask;
    }

    private async Task FetchAndCacheAsync(string claudePath)
    {
        try
        {
            var models = await FetchFromCliAsync(claudePath);
            if (models != null && models.Count > 0)
            {
                _cachedModels = models;
                SaveToFileCache(models);
                _log.Information("Loaded {Count} models from Claude CLI", models.Count);
                Dispatcher.UIThread.Post(() => ModelsUpdated?.Invoke(this, EventArgs.Empty));
            }
            else
            {
                _log.Information("Model fetch returned no results; retaining current list");
            }
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to fetch models from Claude CLI; using current cached list");
        }
    }

    private async Task<IReadOnlyList<ClaudeModelInfo>?> FetchFromCliAsync(string claudePath)
    {
        const string prompt = """
Output ONLY a valid JSON array. No other text before or after.
List all currently available production Claude API models.
Each item must have: "id" (full model ID), "alias" (short CLI alias like "opus"), "displayName" (human-readable name with version, e.g. "Opus 4.7").
Order from most capable to least.
Example: [{"id":"claude-opus-4-7","alias":"opus","displayName":"Opus 4.7"},{"id":"claude-sonnet-4-6","alias":"sonnet","displayName":"Sonnet 4.6"}]
""";

        var rawOutput = await _processManager.RunPrintModeAsync(
            claudePath, prompt, model: "haiku", timeoutMs: 30_000);

        return string.IsNullOrEmpty(rawOutput) ? null : ParseModelsFromOutput(rawOutput);
    }

    private static IReadOnlyList<ClaudeModelInfo>? ParseModelsFromOutput(string rawOutput)
    {
        try
        {
            var json = ExtractJsonArray(rawOutput);
            if (json == null)
                return null;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var result = new List<ClaudeModelInfo>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var id          = item.TryGetProperty("id",          out var idEl)  ? idEl.GetString()  : null;
                var alias       = item.TryGetProperty("alias",       out var alEl)  ? alEl.GetString()  : null;
                var displayName = item.TryGetProperty("displayName", out var dnEl)  ? dnEl.GetString()  : null;

                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(displayName))
                    continue;

                // Guard against hallucinated non-Claude model IDs
                if (!id.StartsWith("claude-", StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(new ClaudeModelInfo(id, alias ?? string.Empty, displayName));
            }

            return result.Count > 0 ? result : null;
        }
        catch (JsonException ex)
        {
            _log.Debug("ParseModelsFromOutput: JSON error: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Extracts the first JSON array from output that may be wrapped in the CLI's
    /// {"type":"result","result":"[...]"} envelope or contain surrounding prose.
    /// </summary>
    private static string? ExtractJsonArray(string text)
    {
        text = text.Trim();

        try
        {
            using var outer = JsonDocument.Parse(text);
            var root = outer.RootElement;

            // CLI --output-format json wraps the response in {"result": "<actual json>"}
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("result", out var resultEl) &&
                resultEl.ValueKind == JsonValueKind.String)
            {
                return ExtractArrayFromText(resultEl.GetString());
            }

            if (root.ValueKind == JsonValueKind.Array)
                return text;
        }
        catch (JsonException)
        {
            // Not valid outer JSON — try to find an array in the raw text
        }

        return ExtractArrayFromText(text);
    }

    private static string? ExtractArrayFromText(string? text)
    {
        if (text == null) return null;

        var start = text.IndexOf('[');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escape = false;

        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (escape) { escape = false; continue; }
            if (c == '\\' && inString) { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '[') depth++;
            else if (c == ']') { depth--; if (depth == 0) return text[start..(i + 1)]; }
        }
        return null;
    }

    private IReadOnlyList<ClaudeModelInfo>? TryLoadFromFileCache()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
                return null;

            var json = File.ReadAllText(_cacheFilePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("timestamp", out var tsEl))
                return null;

            var timestamp = tsEl.GetDateTimeOffset();
            if (DateTimeOffset.UtcNow - timestamp > TimeSpan.FromHours(24))
            {
                _log.Debug("Models cache is stale; will refresh from CLI");
                return null;
            }

            if (!root.TryGetProperty("models", out var modelsEl) ||
                modelsEl.ValueKind != JsonValueKind.Array)
                return null;

            var models = new List<ClaudeModelInfo>();
            foreach (var item in modelsEl.EnumerateArray())
            {
                var id          = item.TryGetProperty("id",          out var idEl) ? idEl.GetString() : null;
                var alias       = item.TryGetProperty("alias",       out var alEl) ? alEl.GetString() : null;
                var displayName = item.TryGetProperty("displayName", out var dnEl) ? dnEl.GetString() : null;

                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(displayName))
                    models.Add(new ClaudeModelInfo(id, alias ?? string.Empty, displayName));
            }

            if (models.Count > 0)
            {
                _log.Debug("Loaded {Count} models from file cache (age < 24h)", models.Count);
                return models;
            }
        }
        catch (Exception ex)
        {
            _log.Debug("Could not load models file cache: {Error}", ex.Message);
        }

        return null;
    }

    private void SaveToFileCache(IReadOnlyList<ClaudeModelInfo> models)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFilePath)!);

            var payload = new
            {
                timestamp = DateTimeOffset.UtcNow,
                models    = models.Select(m => new { m.Id, m.Alias, m.DisplayName }),
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_cacheFilePath, json);
            _log.Debug("Saved {Count} models to file cache", models.Count);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to save models to file cache");
        }
    }
}
