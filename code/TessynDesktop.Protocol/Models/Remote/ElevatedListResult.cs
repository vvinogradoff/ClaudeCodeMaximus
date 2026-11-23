using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Answer of <c>elevated.list</c>: newest first, at most 100.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ElevatedListResult(IReadOnlyList<ElevatedRunDto> Runs);
