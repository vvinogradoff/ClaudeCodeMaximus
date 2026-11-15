using System;
using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <remarks>Created by Claude</remarks>
public sealed class TreeSessionIdSource : ITreeSessionIdSource
{
	private readonly IAppSettingsService _appSettings;

	public TreeSessionIdSource(IAppSettingsService appSettings) => _appSettings = appSettings;

	public IReadOnlySet<string> GetTreeSessionIds()
	{
		var ids = new HashSet<string>(StringComparer.Ordinal);
		lock (_appSettings.SyncRoot)
		{
			foreach (var directory in _appSettings.Settings.Tree)
				Collect(directory.Sessions, directory.Groups, ids);
		}
		return ids;
	}

	private static void Collect(List<SessionNodeModel> sessions, List<GroupNodeModel> groups, HashSet<string> ids)
	{
		foreach (var s in sessions)
		{
			AddIfSet(ids, s.ClaudeSessionId);
			AddIfSet(ids, s.ExternalId);
			foreach (var prior in s.PriorClaudeSessionIds)
				AddIfSet(ids, prior);
		}
		foreach (var g in groups)
			Collect(g.Sessions, g.Groups, ids);
	}

	private static void AddIfSet(HashSet<string> ids, string? id)
	{
		if (!string.IsNullOrEmpty(id))
			ids.Add(id);
	}
}
