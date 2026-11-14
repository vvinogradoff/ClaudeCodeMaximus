using System;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of usage.get (contract section 3.21).</summary>
/// <param name="ProfileId">"default" or a stored profile's id.</param>
/// <param name="ProfileName">The profile's display name.</param>
/// <param name="State">Outcome.</param>
/// <param name="FiveHour">The 5-hour window; present with state "ok".</param>
/// <param name="SevenDay">The 7-day window; present with state "ok".</param>
/// <param name="FetchedAtUtc">When the reading was taken.</param>
/// <remarks>Created by Claude</remarks>
public sealed record UsageResult(
	string ProfileId,
	string ProfileName,
	UsageState State,
	UsageWindowDto? FiveHour,
	UsageWindowDto? SevenDay,
	DateTimeOffset? FetchedAtUtc);
