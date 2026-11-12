using TessynDesktop.Models.Remote;

namespace TessynDesktop.Models;

/// <summary>Outcome of one fetch of the usage endpoint for one profile.</summary>
/// <param name="State">Ok, Unauthorized (HTTP 401) or Unavailable (no token, network or API error).</param>
/// <param name="Data">The reading; present only with <see cref="UsageState.Ok"/>.</param>
/// <remarks>Created by Claude</remarks>
public sealed record UsageFetchResultModel(UsageState State, ClaudeUsageData? Data);
