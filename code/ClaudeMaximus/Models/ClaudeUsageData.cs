using System;

namespace ClaudeMaximus.Models;

/// <summary>
/// Plan utilisation snapshot returned by the Anthropic OAuth usage endpoint (FR.18.4).
/// Both the five-hour rolling window and the seven-day rolling window are captured.
/// </summary>
public sealed record ClaudeUsageData(
    /// <summary>5-hour session-window utilisation (0–100 %).</summary>
    double FiveHourUtilization,
    /// <summary>UTC time at which the 5-hour window resets.</summary>
    DateTimeOffset FiveHourResetsAt,
    /// <summary>Severity string from the API ("normal", "warning", …).</summary>
    string FiveHourSeverity,
    /// <summary>7-day rolling-window utilisation (0–100 %).</summary>
    double SevenDayUtilization,
    /// <summary>UTC time at which the 7-day window resets.</summary>
    DateTimeOffset SevenDayResetsAt,
    /// <summary>Severity string from the API ("normal", "warning", …).</summary>
    string SevenDaySeverity);
