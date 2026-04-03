using System.Collections.Generic;

namespace TessynDesktop.Models;

/// <summary>Result of search RPC.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TessynSearchResponse
{
    public List<TessynSearchResult> Results { get; init; } = [];
    public int Count { get; init; }
}
