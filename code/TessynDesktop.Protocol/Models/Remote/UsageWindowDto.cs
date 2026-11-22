using System;

namespace TessynDesktop.Models.Remote;

/// <summary>One usage window (<c>label</c>, <c>durationMinutes</c> and <c>isExhausted</c> are additive, tdcp-spec 10.A14).</summary>
/// <param name="Utilization">0-100; zero once the window has rolled over.</param>
/// <param name="ResetsAtUtc">When the window resets.</param>
/// <param name="IsStale">The window rolled over since the reading was taken.</param>
/// <param name="Label">The bar's label built from the window's length: <c>5h</c>, <c>7d</c>, or <c>&lt;n&gt;h</c> / <c>&lt;n&gt;d</c> / <c>&lt;n&gt;m</c>. Omitted by an older daemon (then <c>5h</c> and <c>7d</c>).</param>
/// <param name="DurationMinutes">The window's length in minutes (300 and 10080 for Claude; Codex's <c>windowDurationMins</c>).</param>
/// <param name="IsExhausted">True for the window that ran out of credits (a ChatGPT profile with <c>exhausted</c> set): shown in the limits-error colour (FR.33.10). Omitted otherwise.</param>
/// <remarks>Created by Claude</remarks>
public sealed record UsageWindowDto(
	double Utilization,
	DateTimeOffset ResetsAtUtc,
	bool IsStale,
	string? Label = null,
	int? DurationMinutes = null,
	bool? IsExhausted = null);
