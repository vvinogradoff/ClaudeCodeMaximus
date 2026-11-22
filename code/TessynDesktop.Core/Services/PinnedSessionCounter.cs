using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Counts the sessions pinned by id to a profile (ISSUE.013), anywhere in the tree: what <c>profile.remove</c> reports for a Claude or a ChatGPT
/// profile. The caller holds the settings lock.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PinnedSessionCounter
{
	/// <summary>Sessions whose <c>ProfileId</c> is <paramref name="profileId"/> (any case).</summary>
	public int Count(AppSettingsModel settings, string profileId) =>
		settings.Tree.Sum(d => Count(d.Sessions, d.Groups, profileId));

	private int Count(IEnumerable<SessionNodeModel> sessions, IEnumerable<GroupNodeModel> groups, string profileId) =>
		sessions.Count(s => string.Equals(s.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
		+ groups.Sum(g => Count(g.Sessions, g.Groups, profileId));
}
