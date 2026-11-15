using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>import.list</c>.</summary>
/// <param name="Items">The page.</param>
/// <param name="Total">The number of sessions matching before paging.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ImportListResult(IReadOnlyList<ImportCandidateDto> Items, int Total);
