using System;
using System.Collections.Generic;
using TessynDesktop.Models.Agent;

namespace TessynDesktop.Services;

/// <summary>Per-call state shared by the steps of <see cref="SessionListPageBuilder"/> (FR.15.13).</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class SessionListBuildContext
{
	private readonly Func<SessionSnapshotModel, DateTimeOffset?> _getLastActivityUtc;
	private readonly Dictionary<string, DateTimeOffset?> _lastActivity = new(StringComparer.OrdinalIgnoreCase);

	public SessionListBuildContext(
		SessionListIndex index,
		SessionSnapshotModel? caller,
		SessionSnapshotModel? scopeRoot,
		SessionListQueryModel query,
		Func<SessionSnapshotModel, DateTimeOffset?> getLastActivityUtc,
		DateTimeOffset nowUtc)
	{
		Index                = index;
		Caller               = caller;
		ScopeRoot            = scopeRoot;
		Query                = query;
		Now                  = nowUtc;
		_getLastActivityUtc  = getLastActivityUtc;
	}

	public SessionListIndex Index { get; }

	public SessionSnapshotModel? Caller { get; }

	/// <summary>The root session of a children/subtree scope; null for project/all.</summary>
	public SessionSnapshotModel? ScopeRoot { get; }

	public SessionListQueryModel Query { get; }

	public DateTimeOffset Now { get; }

	/// <summary>Last Activity of <paramref name="s"/>, looked up at most once per call.</summary>
	public DateTimeOffset? GetLastActivity(SessionSnapshotModel s)
	{
		if (_lastActivity.TryGetValue(s.NodeId, out var cached))
			return cached;

		var value = _getLastActivityUtc(s);
		_lastActivity[s.NodeId] = value;
		return value;
	}

	/// <summary>Newest Last Activity first, ties by name and then NodeId; sessions without a file come last.</summary>
	public int CompareNewestFirst(SessionSnapshotModel a, SessionSnapshotModel b)
	{
		var la = GetLastActivity(a);
		var lb = GetLastActivity(b);

		if (la.HasValue != lb.HasValue)
			return la.HasValue ? -1 : 1;

		if (la.HasValue && la.Value != lb!.Value)
			return lb.Value.CompareTo(la.Value);

		var byName = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
		if (byName != 0)
			return byName;

		return string.CompareOrdinal(a.NodeId, b.NodeId);
	}
}
