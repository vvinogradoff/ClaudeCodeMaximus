using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClaudeMaximus.Models;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class OllamaModelService : IOllamaModelService
{
    private static readonly ILogger _log = Log.ForContext<OllamaModelService>();

    public async Task<IReadOnlyList<ClaudeModelInfo>> GetModelsAsync(
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromMilliseconds(Constants.Ollama.DiscoveryTimeoutMs),
            };
            var url = baseUrl.TrimEnd('/') + Constants.Ollama.TagsPath;
            var json = await http.GetStringAsync(url, cancellationToken);
            var result = ParseModels(json);
            if (result.Count > 0)
                _log.Debug("OllamaModelService: found {Count} models at {Url}", result.Count, url);
            return result;
        }
        catch (Exception ex)
        {
            _log.Debug("OllamaModelService: discovery failed (Ollama not running?): {Error}", ex.Message);
            return [];
        }
    }

    private static IReadOnlyList<ClaudeModelInfo> ParseModels(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("models", out var modelsEl) ||
                modelsEl.ValueKind != JsonValueKind.Array)
                return [];

            var result = new List<ClaudeModelInfo>();
            foreach (var item in modelsEl.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (string.IsNullOrEmpty(name))
                    continue;
                result.Add(new ClaudeModelInfo(name, string.Empty, name, ModelProvider.Ollama));
            }
            return result;
        }
        catch (JsonException ex)
        {
            _log.Debug("OllamaModelService: JSON parse error: {Error}", ex.Message);
            return [];
        }
    }
}
