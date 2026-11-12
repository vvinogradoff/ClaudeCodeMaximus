using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Builds the directory-grouped session lists of the Recent tab (FR.1.16) and the Tags tab (FR.24.12) over a tree snapshot:
/// top-level sessions only, directories in tree order (empty ones skipped), drafts first, then newest Last Activity first
/// (sessions without activity last). The same rule as <c>SessionGroupListBuilder</c> in the desktop project, which is not refactored for this.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class SessionGroupBuilder
{
	/// <param name="snapshot">The tree copy.</param>
	/// <param name="labelOf">Display label of a directory path.</param>
	/// <param name="include">Which top-level sessions to list (by NodeId).</param>
	/// <param name="activityOf">Activity of a session (draft flag and Last Activity).</param>
	/// <param name="stateOf">Live state of a session.</param>
	public static IReadOnlyList<SessionGroupDto> Build(
		RemoteTreeSnapshotModel snapshot,
		Func<string, string> labelOf,
		Func<string, bool> include,
		Func<RemoteSessionRecord, SessionActivityModel> activityOf,
		Func<RemoteSessionRecord, SessionStateDto> stateOf)
	{
		var byDirectory = snapshot.TopLevelSessions()
			.Where(s => include(s.NodeId))
			.ToLookup(s => s.DirectoryId, StringComparer.Ordinal);

		var groups = new List<SessionGroupDto>();
		foreach (var directory in snapshot.Directories)
		{
			var sessions = byDirectory[directory.Id]
				.Select(s => (Session: s, Activity: activityOf(s)))
				.OrderByDescending(x => x.Activity.HasDraft)
				.ThenByDescending(x => x.Activity.LastActivityUtc ?? DateTimeOffset.MinValue)
				.Select(x => new SessionRowDto(x.Session.NodeId, x.Session.Name, stateOf(x.Session)))
				.ToList();

			if (sessions.Count > 0)
				groups.Add(new SessionGroupDto(directory.Id, labelOf(directory.Path), sessions));
		}

		return groups;
	}
}
