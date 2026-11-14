namespace TessynDesktop.Models.Remote;

/// <summary>Outcome of a usage lookup; serialized as "ok", "pending", "unauthorized" or "unavailable".</summary>
/// <remarks>Created by Claude</remarks>
public enum UsageState
{
	/// <summary>Data is present (possibly an older reading when the latest fetch failed).</summary>
	Ok,

	/// <summary>The first fetch for the profile is still running.</summary>
	Pending,

	/// <summary>The API rejected the profile's token.</summary>
	Unauthorized,

	/// <summary>No token, no network, or an API error, and no earlier reading.</summary>
	Unavailable,
}
