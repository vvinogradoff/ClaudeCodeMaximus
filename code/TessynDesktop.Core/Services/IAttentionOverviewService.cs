using System.Collections.Generic;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>The attention items of every session in one list (FR.23): the cross-session view behind <c>attention.listAll</c>.</summary>
/// <remarks>Created by Claude</remarks>
public interface IAttentionOverviewService
{
	/// <summary>
	/// One page of the items of all sessions with the given status (default: open) and kinds (default: all), most urgent first, then newest.
	/// <paramref name="afterId"/> is the <see cref="AttentionListAllResult.NextAfterId"/> of the previous page.
	/// </summary>
	AttentionListAllResult ListAll(string? status, IReadOnlyList<string>? kinds, int? limit, string? afterId);
}
