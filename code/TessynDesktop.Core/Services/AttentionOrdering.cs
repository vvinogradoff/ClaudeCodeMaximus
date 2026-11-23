using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// The one ordering used wherever Attention Items are listed (FR.23.2): open items first, by kind
/// severity then number ascending, the in-progress ones (FR.23.41) after the other open items; then closed items, newest <c>ClosedUtc</c> first.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class AttentionOrdering
{
	/// <summary>
	/// FR.23.41: the item is open and the elevated run behind it is launching or running, so it is an approved action, not a question. The one test every badge
	/// count, top kind and pinned-list line uses.
	/// </summary>
	public static bool IsInProgress(AttentionItemModel item) => item.Status == AttentionStatus.Open && item.IsRunInProgress;

	/// <summary>FR.23.13, FR.23.17: an open item that is not in progress counts toward the badges.</summary>
	public static bool IsCounted(AttentionItemModel item) => item.Status == AttentionStatus.Open && !IsInProgress(item);

	public static List<AttentionItemModel> Sort(IEnumerable<AttentionItemModel> items) =>
		items
			.OrderBy(i => i.Status == AttentionStatus.Open ? 0 : 1)
			.ThenBy(i => IsInProgress(i) ? 1 : 0)
			.ThenBy(i => i.Status == AttentionStatus.Open ? (int)i.Kind : 0)
			.ThenBy(i => i.Status == AttentionStatus.Open ? i.Number : 0)
			.ThenByDescending(i => i.ClosedUtc)
			.ThenBy(i => i.Number)
			.ToList();
}
