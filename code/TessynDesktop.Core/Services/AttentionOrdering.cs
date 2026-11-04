using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The one ordering used wherever Attention Items are listed (FR.23.2): open items first, by kind
/// severity then number ascending; then closed items, newest <c>ClosedUtc</c> first.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class AttentionOrdering
{
	public static List<AttentionItemModel> Sort(IEnumerable<AttentionItemModel> items) =>
		items
			.OrderBy(i => i.Status == AttentionStatus.Open ? 0 : 1)
			.ThenBy(i => i.Status == AttentionStatus.Open ? (int)i.Kind : 0)
			.ThenBy(i => i.Status == AttentionStatus.Open ? i.Number : 0)
			.ThenByDescending(i => i.ClosedUtc)
			.ThenBy(i => i.Number)
			.ToList();
}
