using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>The cross-project search of Claude sessions (FR.13) and the rescan of the session index behind it.</summary>
/// <remarks>Created by Claude</remarks>
public interface IGlobalSearchService
{
	/// <summary>One page of the sessions whose messages match <paramref name="query"/>, best first, one hit per session, with the tree node that already imports the session.</summary>
	Task<SearchGlobalResult> SearchAsync(string query, string? projectSlug, int? limit, int? offset, CancellationToken cancellationToken = default);

	/// <summary>Starts a rescan of the session index in the background; false when one is already running.</summary>
	SearchReindexResult StartReindex();
}
