using System;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of usage.get (contract section 3.21; <c>engine</c> and <c>exhausted</c> are additive, tdcp-spec 10.A14).</summary>
/// <param name="ProfileId">"default" or a stored profile's id; a ChatGPT profile's <c>codex-default</c> or <c>codex_N</c>.</param>
/// <param name="ProfileName">The profile's display name.</param>
/// <param name="State">Outcome.</param>
/// <param name="FiveHour">The 5-hour window; present with state "ok". For a ChatGPT profile Codex's <c>primary</c> window, whatever its length (see its <c>label</c>).</param>
/// <param name="SevenDay">The 7-day window; present with state "ok". For a ChatGPT profile Codex's <c>secondary</c> window; null when the plan has none.</param>
/// <param name="FetchedAtUtc">When the reading was taken.</param>
/// <param name="Engine"><c>codex</c> for a ChatGPT profile; omitted for a Claude profile.</param>
/// <param name="Exhausted">A ChatGPT profile's <c>rateLimitReachedType</c> while the plan is out of credits; omitted otherwise.</param>
/// <remarks>Created by Claude</remarks>
public sealed record UsageResult(
	string ProfileId,
	string ProfileName,
	UsageState State,
	UsageWindowDto? FiveHour,
	UsageWindowDto? SevenDay,
	DateTimeOffset? FetchedAtUtc,
	string? Engine = null,
	string? Exhausted = null);
