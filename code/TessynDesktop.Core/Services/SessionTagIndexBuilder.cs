using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Pure builder of the Session Tag Index (FR.24.13): rolls Worker Sessions up to their top-level supervisor,
/// scopes by the top-level session's Directory node path, aggregates by canonical key and picks the
/// representative copy of each tag.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class SessionTagIndexBuilder
{
	/// <param name="sessions">Tree snapshot (every session, workers included).</param>
	/// <param name="tagsOf">Tags of a session, given its snapshot (name-derived plus declared, as the Tag row shows them).</param>
	/// <param name="directoryPath">Scope: the top-level session's Directory node path (OrdinalIgnoreCase); null = all.</param>
	public static IReadOnlyList<SessionTagUsageModel> Build(
		IReadOnlyList<SessionTagIndexSessionModel> sessions,
		Func<SessionTagIndexSessionModel, IReadOnlyList<SessionTagModel>> tagsOf,
		string? directoryPath)
	{
		var byId = new Dictionary<string, SessionTagIndexSessionModel>(StringComparer.Ordinal);
		foreach (var s in sessions)
			byId.TryAdd(s.NodeId, s);

		var usages = new Dictionary<string, SessionTagUsageModel>(StringComparer.OrdinalIgnoreCase);
		var seen   = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);   // per-usage distinct NodeIds

		foreach (var session in sessions)
		{
			var top = ResolveTopLevel(session, byId);
			if (directoryPath != null && !string.Equals(top.DirectoryPath, directoryPath, StringComparison.OrdinalIgnoreCase))
				continue;

			foreach (var tag in tagsOf(session))
			{
				if (!usages.TryGetValue(tag.Key, out var usage))
				{
					usages[tag.Key] = new SessionTagUsageModel { Tag = tag, NodeIds = [top.NodeId] };
					seen[tag.Key]   = new HashSet<string>(StringComparer.Ordinal) { top.NodeId };
					continue;
				}

				if (seen[tag.Key].Add(top.NodeId))
					usage.NodeIds.Add(top.NodeId);
				if (IsBetterRepresentative(tag, usage.Tag))
					usage.Tag = tag;
			}
		}

		return usages.Values.ToList();
	}

	/// <summary>
	/// Follows SupervisorNodeId to a session without one. A missing supervisor stops at the last session found.
	/// On a cycle the member with the lowest NodeId (Ordinal) is the top-level session, so every session in or
	/// leading into the same cycle rolls up to the same id whichever one the walk starts from.
	/// </summary>
	private static SessionTagIndexSessionModel ResolveTopLevel(SessionTagIndexSessionModel start, Dictionary<string, SessionTagIndexSessionModel> byId)
	{
		var path    = new List<SessionTagIndexSessionModel> { start };
		var indexOf = new Dictionary<string, int>(StringComparer.Ordinal) { [start.NodeId] = 0 };
		var current = start;
		while (!string.IsNullOrEmpty(current.SupervisorNodeId) && byId.TryGetValue(current.SupervisorNodeId, out var next))
		{
			if (indexOf.TryGetValue(next.NodeId, out var cycleStart))
			{
				var best = path[cycleStart];
				for (var i = cycleStart + 1; i < path.Count; i++)
					if (string.CompareOrdinal(path[i].NodeId, best.NodeId) < 0)
						best = path[i];
				return best;
			}
			indexOf[next.NodeId] = path.Count;
			path.Add(next);
			current = next;
		}
		return current;
	}

	/// <summary>Name-derived beats declared; between declared copies the most recently added wins; otherwise the first seen stays.</summary>
	private static bool IsBetterRepresentative(SessionTagModel candidate, SessionTagModel current)
	{
		if (current.Source == SessionTagSource.Name)
			return false;
		if (candidate.Source == SessionTagSource.Name)
			return true;
		return (candidate.AddedUtc ?? DateTimeOffset.MinValue) > (current.AddedUtc ?? DateTimeOffset.MinValue);
	}
}
