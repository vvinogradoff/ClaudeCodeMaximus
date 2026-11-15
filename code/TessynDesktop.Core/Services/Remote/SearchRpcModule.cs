using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>search.global</c> and <c>search.reindex</c> (package J4, FR.13): the cross-project search of Claude sessions. Loopback only
/// (<c>remoteCallable: false</c>): hits name the Claude projects of this machine and the tree nodes that import them.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SearchRpcModule : IRemoteRpcModule
{
	private readonly IGlobalSearchService _search;

	public SearchRpcModule(IGlobalSearchService search)
	{
		_search = search;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.Add(RemoteProtocol.Search.GlobalMethod, async (p, ct) => await GlobalAsync(p, ct), remoteCallable: false);
		registry.AddSync(RemoteProtocol.Search.ReindexMethod, _ => _search.StartReindex(), remoteCallable: false);
	}

	private async Task<object?> GlobalAsync(JsonNode? @params, CancellationToken cancellationToken) =>
		await _search.SearchAsync(
			TdcpParams.RequireString(@params, "query"),
			TdcpParams.OptionalString(@params, "projectSlug"),
			TdcpParams.OptionalInt(@params, "limit"),
			TdcpParams.OptionalInt(@params, "offset"),
			cancellationToken);
}
