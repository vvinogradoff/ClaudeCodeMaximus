using System;

namespace TessynDesktop.Models;

/// <summary>
/// The Codex plan windows of one ChatGPT profile (FR.33.10): the <c>codex</c> bucket, <see cref="Primary"/> on top and <see cref="Secondary"/>
/// below; a window that is null is hidden (Pro plans have no short window). <see cref="RateLimitReachedType"/> is set when the plan is out of credits.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record CodexUsageReadingModel(
	string ProfileId,
	CodexUsageWindowModel? Primary,
	CodexUsageWindowModel? Secondary,
	string? PlanType,
	string? RateLimitReachedType,
	DateTimeOffset FetchedAtUtc);
