using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
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
            var trimmedBase = baseUrl.TrimEnd('/');
            var tagsUrl = trimmedBase + Constants.Ollama.TagsPath;
            var json = await http.GetStringAsync(tagsUrl, cancellationToken);
            var models = ParseModels(json);
            if (models.Count == 0)
                return models;

            _log.Debug("OllamaModelService: found {Count} models at {Url}", models.Count, tagsUrl);

            // Check tool support for each model via /api/show in parallel
            var showUrl = trimmedBase + Constants.Ollama.ShowPath;
            var tasks = models.Select(m => CheckToolsSupportAsync(http, showUrl, m, cancellationToken));
            var results = await Task.WhenAll(tasks);
            return results;
        }
        catch (Exception ex)
        {
            _log.Debug("OllamaModelService: discovery failed (Ollama not running?): {Error}", ex.Message);
            return [];
        }
    }

    private async Task<ClaudeModelInfo> CheckToolsSupportAsync(
        HttpClient http,
        string showUrl,
        ClaudeModelInfo model,
        CancellationToken cancellationToken)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { name = model.Id });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Constants.Ollama.ShowTimeoutMs);
            var response = await http.PostAsync(showUrl, content, cts.Token);
            var responseJson = await response.Content.ReadAsStringAsync(cts.Token);
            var supportsTools = ParseCapabilitiesHasTools(responseJson);
            _log.Debug("OllamaModelService: {Model} SupportsTools={SupportsTools}", model.Id, supportsTools);
            return model with { SupportsTools = supportsTools };
        }
        catch (Exception ex)
        {
            _log.Debug("OllamaModelService: /api/show failed for {Model}: {Error}", model.Id, ex.Message);
            return model with { SupportsTools = false };
        }
    }

    private static bool ParseCapabilitiesHasTools(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("capabilities", out var caps) ||
                caps.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var cap in caps.EnumerateArray())
            {
                if (string.Equals(cap.GetString(), "tools", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
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
                // SupportsTools defaults to true here; CheckToolsSupportAsync will override with the real value.
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
