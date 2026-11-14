using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>recent.list</c> (contract 3.6).</summary>
/// <param name="ServerTimeUtc">The daemon's clock, for the client's own relative-time display.</param>
/// <param name="Sessions">Pinned or active-within-24-hours sessions, newest Last Activity first (none last).</param>
/// <remarks>Created by Claude</remarks>
public sealed record RecentListResult(DateTimeOffset ServerTimeUtc, IReadOnlyList<RecentRowDto> Sessions);
