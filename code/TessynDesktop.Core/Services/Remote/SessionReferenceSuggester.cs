using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models.Agent;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The session suggestions of <c>#</c> while Code references is off (FR.7.14): sessions whose name contains the query, in three tiers (same group,
/// same project, other projects while fewer than <see cref="Constants.Composer.SessionOtherProjectsBelow"/> were found), Worker Sessions and the
/// current session left out. Served from the registry snapshot: no file access and no code index.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionReferenceSuggester
{
	private readonly ISessionRegistry _registry;

	public SessionReferenceSuggester(ISessionRegistry registry) => _registry = registry;

	public IReadOnlyList<AutocompleteItemDto> Suggest(string currentNodeId, string query)
	{
		var snapshots = _registry.SnapshotSessionsForListing();
		var index     = new SessionListIndex(snapshots);
		var current   = snapshots.FirstOrDefault(s => string.Equals(s.NodeId, currentNodeId, StringComparison.OrdinalIgnoreCase));

		var matches = new List<Candidate>();
		foreach (var s in snapshots)
		{
			if (index.IsWorker(s) || string.Equals(s.NodeId, currentNodeId, StringComparison.OrdinalIgnoreCase))
				continue;

			var at = s.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase);
			if (at >= 0)
				matches.Add(new Candidate(s, at, TierOf(s, current)));
		}

		var nearby = matches.Count(m => m.Tier < OtherProjectsTier);
		var chosen = matches
			.Where(m => m.Tier < OtherProjectsTier || nearby < Constants.Composer.SessionOtherProjectsBelow)
			.OrderBy(m => m.Tier)
			.ThenBy(m => m.MatchStart == 0 ? 0 : 1)
			.ThenBy(m => m.Session.IsFavorite ? 0 : 1)
			.ThenBy(m => m.Session.TreeOrder)
			.Take(Constants.CodeIndex.MaxSuggestions);

		return chosen.Select(m => ToItem(m, index, query)).ToList();
	}

	private const int OtherProjectsTier = 3;

	private static int TierOf(SessionSnapshotModel s, SessionSnapshotModel? current)
	{
		if (current == null || !string.Equals(s.DirectoryPath, current.DirectoryPath, StringComparison.OrdinalIgnoreCase))
			return OtherProjectsTier;

		// A session directly under the Directory Node (no group) belongs to the one "loose" group of that node.
		return string.Equals(s.GroupNodeId, current.GroupNodeId, StringComparison.OrdinalIgnoreCase) ? 1 : 2;
	}

	private static AutocompleteItemDto ToItem(Candidate m, SessionListIndex index, string query)
	{
		var s      = m.Session;
		var detail = s.GroupPath.Count == 0
			? s.DirectoryLabel
			: s.DirectoryLabel + Constants.Composer.SessionDetailSeparator + string.Join(Constants.Composer.SessionDetailSeparator, s.GroupPath);

		return new AutocompleteItemDto(
			s.Name, string.Format(Constants.Composer.SessionInsertFormat, s.Name, index.GetShortId(s.NodeId)),
			Constants.Composer.SessionKind, detail, m.MatchStart, query.Length);
	}

	private sealed record Candidate(SessionSnapshotModel Session, int MatchStart, int Tier);
}
