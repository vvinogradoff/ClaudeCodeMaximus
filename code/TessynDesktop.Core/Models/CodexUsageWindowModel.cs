using System;

namespace TessynDesktop.Models;

/// <summary>
/// One Codex plan window (FR.33.10): its label built from <c>windowDurationMins</c> (<c>5h</c>, <c>7d</c>, never assumed), the used percent, the
/// reset time, whether it rolled over since the reading (FR.18.8; then shown as 0) and whether it is the exhausted one.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexUsageWindowModel(string Label, double UsedPercent, DateTimeOffset? ResetsAtUtc, int? WindowDurationMins, bool IsStale, bool IsExhausted);
