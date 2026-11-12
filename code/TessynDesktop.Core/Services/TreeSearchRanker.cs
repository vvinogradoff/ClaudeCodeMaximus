using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Pure tree-search ranking (FR.1.9, FR.1.12) over a flat session list: a top-level session matches when it or any
/// Worker Session below it matches, its key is the best of those keys, and the matches are ordered best-first with
/// <see cref="SearchRelevanceComparer"/>. Equal keys keep tree order.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class TreeSearchRanker
{
	private static readonly SearchRelevanceComparer Comparer = new();

	/// <param name="sessions">Every session of the tree (workers included) in tree order.</param>
	/// <param name="scoreOf">The search score of a session, or null when it does not match.</param>
	/// <param name="lastPromptOf">Last prompt time of a session NodeId (tiebreak); called only for matching sessions.</param>
	public static IReadOnlyList<TreeSearchMatchDto> Rank(
		IReadOnlyList<RemoteSessionRecord> sessions,
		Func<RemoteSessionRecord, SessionSearchScoreModel?> scoreOf,
		Func<string, DateTimeOffset?> lastPromptOf)
	{
		var byId = new Dictionary<string, RemoteSessionRecord>(sessions.Count, StringComparer.Ordinal);
		foreach (var s in sessions)
			byId.TryAdd(s.NodeId, s);

		var groups = new Dictionary<string, Candidate>(StringComparer.Ordinal);
		var order  = new List<Candidate>();

		foreach (var session in sessions)
		{
			var score = scoreOf(session);
			if (score == null)
				continue;

			var key = new SearchRelevanceKeyModel(score.StrictMatchCount, score.LooseMatchCount, lastPromptOf(session.NodeId));
			var top = ResolveTopLevel(session, byId);

			if (!groups.TryGetValue(top.NodeId, out var candidate))
			{
				candidate = new Candidate(top.NodeId);
				groups[top.NodeId] = candidate;
				order.Add(candidate);
			}

			if (session.NodeId == top.NodeId)
				candidate.MatchedSelf = true;
			else
				candidate.WorkerIds.Add(session.NodeId);

			if (candidate.Key == null || Comparer.Compare(key, candidate.Key) < 0)
				candidate.Key = key;
		}

		return order
			.OrderBy(c => c.Key, Comparer)   // OrderBy is stable: equal keys keep tree order
			.Select(c => new TreeSearchMatchDto(c.Id, c.Key!.StrictMatchCount, c.Key.LooseMatchCount, c.MatchedSelf, c.WorkerIds))
			.ToList();
	}

	/// <summary>Follows SupervisorNodeId up to a session without a (known) supervisor; a cycle ends at the first repeated session.</summary>
	private static RemoteSessionRecord ResolveTopLevel(RemoteSessionRecord start, Dictionary<string, RemoteSessionRecord> byId)
	{
		var current = start;
		var visited = new HashSet<string>(StringComparer.Ordinal) { start.NodeId };
		while (!string.IsNullOrEmpty(current.SupervisorNodeId)
			&& byId.TryGetValue(current.SupervisorNodeId, out var next)
			&& visited.Add(next.NodeId))
		{
			current = next;
		}
		return current;
	}

	private sealed class Candidate(string id)
	{
		public string Id { get; } = id;
		public SearchRelevanceKeyModel? Key { get; set; }
		public bool MatchedSelf { get; set; }
		public List<string> WorkerIds { get; } = [];
	}
}
