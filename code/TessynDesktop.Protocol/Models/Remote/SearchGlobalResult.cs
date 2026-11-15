using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>search.global</c>: one page of sessions, best first.</summary>
/// <param name="Hits">The page.</param>
/// <param name="Total">The sessions found among the best-ranked messages of the index (the search looks at a bounded number of messages); not a count of the whole index.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SearchGlobalResult(IReadOnlyList<SearchHitDto> Hits, int Total);
