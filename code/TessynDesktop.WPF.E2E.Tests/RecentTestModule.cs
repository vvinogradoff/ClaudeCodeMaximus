using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.WPF.E2E.Tests;

/// <summary>
/// A test-only <c>recent.list</c>, <c>recent.hide</c> and <c>tags.search</c> over the sessions of the test daemon: every given session is listed
/// (a favourite is pinned and cannot be hidden), hidden ones are dropped. The real Recent module needs scheduler and attention services the
/// E2E host does not have; the favourite flag is the real one (<c>session.setFavorite</c> sets it on the same node object).
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecentTestModule : IRemoteRpcModule
{
	private readonly IReadOnlyList<SessionNodeModel> _sessions;
	private readonly HashSet<string> _hidden = [];

	public RecentTestModule(IReadOnlyList<SessionNodeModel> sessions)
	{
		_sessions = sessions;
	}

	/// <summary>The node ids hidden so far.</summary>
	public IReadOnlyCollection<string> Hidden
	{
		get
		{
			lock (_hidden)
				return _hidden.ToList();
		}
	}

	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync("recent.list", _ => List(), remoteCallable: true);
		registry.AddSync("recent.hide", Hide, remoteCallable: true);
		registry.AddSync("tags.search", _ => new TagSearchResult(TagEmptyReason.NoTagsUnscoped, [], TagSessionsEmptyReason.SelectTags, []), remoteCallable: true);
	}

	private RecentListResult List()
	{
		lock (_hidden)
		{
			var rows = _sessions
				.Where(s => s.IsFavorite || !_hidden.Contains(s.NodeId))
				.OrderByDescending(s => s.IsFavorite)
				.Select(s => new RecentRowDto(s.NodeId, s.Name, "proj", new SessionStateDto(s.NodeId, false, false, null, s.IsFavorite, DateTimeOffset.UtcNow, null, false, null),
					false, s.IsFavorite, !s.IsFavorite))
				.ToList();
			return new RecentListResult(DateTimeOffset.UtcNow, rows);
		}
	}

	private object? Hide(JsonNode? @params)
	{
		var id = TdcpParams.RequireString(@params, "nodeId");
		var session = _sessions.FirstOrDefault(s => s.NodeId == id);
		if (session == null || session.IsFavorite)
			return new RecentHideResult(false);

		lock (_hidden)
			_hidden.Add(id);
		return new RecentHideResult(true);
	}
}
