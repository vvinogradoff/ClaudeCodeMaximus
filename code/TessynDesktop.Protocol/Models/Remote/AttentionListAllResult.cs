using System;
using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of <c>attention.listAll</c>: one page of the items of every session, most urgent first.</summary>
/// <param name="Items">The page.</param>
/// <param name="NextAfterId">The <c>afterId</c> of the next page; null on the last page.</param>
/// <remarks>Created by Claude</remarks>
public sealed record AttentionListAllResult(IReadOnlyList<AttentionAllItemDto> Items, string? NextAfterId);
