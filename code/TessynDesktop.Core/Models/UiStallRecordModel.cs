using System;

namespace TessynDesktop.Models;

/// <summary>A finished UI-thread stall observed by the watchdog.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record UiStallRecordModel(
	DateTimeOffset StartedAt,
	double DurationMs,
	string? Operation,
	int Gen2Delta,
	double GcPauseDeltaMs);
