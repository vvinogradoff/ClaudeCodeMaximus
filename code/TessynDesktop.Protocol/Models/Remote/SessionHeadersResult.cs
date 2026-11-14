using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of the loopback-only <c>session.headers</c> (P1.G).</summary>
/// <param name="Headers">One header per known session, in the order of the request (duplicates removed).</param>
/// <param name="Missing">Requested NodeIds that are not sessions; omitted when none.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionHeadersResult(IReadOnlyList<SessionHeaderDto> Headers, IReadOnlyList<string>? Missing = null);
