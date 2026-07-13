using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClaudeMaximus.Models;
using Serilog;

namespace ClaudeMaximus.Services;

/// <remarks>Created by Claude</remarks>
public sealed class ClaudeUsageService : IClaudeUsageService
{
    private static readonly ILogger _log = Log.ForContext<ClaudeUsageService>();
    private const string UsageEndpoint = "https://api.anthropic.com/api/oauth/usage";
    private const string BetaHeader    = "anthropic-beta";
    private const string BetaValue     = "oauth-2025-04-20";

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, ClaudeUsageData> _profileCache = new();
    private CancellationTokenSource _cts = new();
    private string? _activeCredentialsPath;

    public ClaudeUsageData? CachedUsage { get; private set; }

    public event EventHandler? UsageUpdated;

    public ClaudeUsageService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add(BetaHeader, BetaValue);
    }

    public void SetActiveProfile(string? credentialsFilePath)
    {
        _activeCredentialsPath = credentialsFilePath;

        // Cancel previous polling loop
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        // Restore per-profile cached data immediately (null = no previous data = show zeros)
        CachedUsage = credentialsFilePath != null && _profileCache.TryGetValue(credentialsFilePath, out var cached)
            ? cached
            : null;

        // Notify UI of the profile switch immediately so bars reflect new state
        Dispatcher.UIThread.Post(() => UsageUpdated?.Invoke(this, EventArgs.Empty));

        if (credentialsFilePath != null)
            _ = RunPollingLoopAsync(credentialsFilePath, _cts.Token);
    }

    private async Task RunPollingLoopAsync(string credentialsPath, CancellationToken ct)
    {
        // Immediate first fetch
        await RefreshAsync(credentialsPath, ct);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Constants.Usage.PollingIntervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await RefreshAsync(credentialsPath, ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task RefreshAsync(string credentialsPath, CancellationToken ct)
    {
        var token = ReadAccessToken(credentialsPath);
        if (token == null)
        {
            _log.Debug("ClaudeUsageService: no access token found at {Path}", credentialsPath);
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _log.Debug("ClaudeUsageService: 401 Unauthorized — stopping polling, keeping cached data");
                // Cancel future polls for this profile (token is dead)
                _cts.Cancel();
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                _log.Debug("ClaudeUsageService: HTTP {Code} — will retry next interval", (int)response.StatusCode);
                return;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var usage = ParseUsage(json);

            if (usage != null)
            {
                CachedUsage = usage;
                _profileCache[credentialsPath] = usage;
                Dispatcher.UIThread.Post(() => UsageUpdated?.Invoke(this, EventArgs.Empty));
                _log.Debug("ClaudeUsageService: refreshed — 5h={FiveH:0}%  7d={SevenD:0}%",
                    usage.FiveHourUtilization, usage.SevenDayUtilization);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.Debug(ex, "ClaudeUsageService: fetch failed");
        }
    }

    private static ClaudeUsageData? ParseUsage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("five_hour", out var fiveHour) ||
                !root.TryGetProperty("seven_day", out var sevenDay))
                return null;

            var fiveHourUtil   = fiveHour.TryGetProperty("utilization", out var fu) ? fu.GetDouble() : 0.0;
            var fiveHourResets = fiveHour.TryGetProperty("resets_at",   out var fr) && fr.ValueKind != JsonValueKind.Null
                ? fr.GetDateTimeOffset() : DateTimeOffset.UtcNow.AddHours(5);

            var sevenDayUtil   = sevenDay.TryGetProperty("utilization", out var su) ? su.GetDouble() : 0.0;
            var sevenDayResets = sevenDay.TryGetProperty("resets_at",   out var sr) && sr.ValueKind != JsonValueKind.Null
                ? sr.GetDateTimeOffset() : DateTimeOffset.UtcNow.AddDays(7);

            // Extract severity from limits[] (group="session" → 5h, group="weekly" → 7d)
            var fiveHourSeverity = "normal";
            var sevenDaySeverity = "normal";

            if (root.TryGetProperty("limits", out var limits) &&
                limits.ValueKind == JsonValueKind.Array)
            {
                foreach (var limit in limits.EnumerateArray())
                {
                    if (!limit.TryGetProperty("group", out var grpEl)) continue;
                    var group    = grpEl.GetString() ?? "";
                    var severity = limit.TryGetProperty("severity", out var sevEl) ? sevEl.GetString() ?? "normal" : "normal";

                    if (group == "session") fiveHourSeverity = severity;
                    else if (group == "weekly") sevenDaySeverity = severity;
                }
            }

            return new ClaudeUsageData(
                fiveHourUtil, fiveHourResets, fiveHourSeverity,
                sevenDayUtil, sevenDayResets, sevenDaySeverity);
        }
        catch (Exception ex)
        {
            _log.Debug("ClaudeUsageService.ParseUsage: {Error}", ex.Message);
            return null;
        }
    }

    private static string? ReadAccessToken(string credentialsPath)
    {
        try
        {
            if (!File.Exists(credentialsPath))
                return null;

            var json = File.ReadAllText(credentialsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("claudeAiOauth", out var oauth) &&
                oauth.TryGetProperty("accessToken",  out var token))
                return token.GetString();
        }
        catch (Exception ex)
        {
            _log.Debug("ClaudeUsageService.ReadAccessToken: {Error}", ex.Message);
        }

        return null;
    }
}
