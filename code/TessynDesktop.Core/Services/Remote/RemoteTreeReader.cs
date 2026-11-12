using System.Collections.Generic;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IRemoteTreeReader" />
/// <remarks>Created by Claude</remarks>
public sealed class RemoteTreeReader : IRemoteTreeReader
{
	private readonly IAppSettingsService _appSettings;

	public RemoteTreeReader(IAppSettingsService appSettings)
	{
		_appSettings = appSettings;
	}

	/// <inheritdoc />
	public RemoteTreeSnapshotModel Snapshot()
	{
		var all         = new List<RemoteSessionRecord>();
		var directories = new List<RemoteDirectoryRecord>();

		lock (_appSettings.SyncRoot)
		{
			foreach (var dir in _appSettings.Settings.Tree)
			{
				var groups   = CopyGroups(dir.Groups, dir, all);
				var sessions = CopySessions(dir.Sessions, dir, all);
				directories.Add(new RemoteDirectoryRecord(dir.NodeId, dir.Path, dir.IsExpanded, groups, sessions));
			}
		}

		return new RemoteTreeSnapshotModel(directories, all);
	}

	private static List<RemoteGroupRecord> CopyGroups(List<GroupNodeModel> groups, DirectoryNodeModel dir, List<RemoteSessionRecord> all)
	{
		var result = new List<RemoteGroupRecord>(groups.Count);
		foreach (var group in groups)
		{
			var children = CopyGroups(group.Groups, dir, all);
			var sessions = CopySessions(group.Sessions, dir, all);
			result.Add(new RemoteGroupRecord(group.NodeId, group.Name, group.IsExpanded, children, sessions));
		}
		return result;
	}

	private static List<RemoteSessionRecord> CopySessions(List<SessionNodeModel> sessions, DirectoryNodeModel dir, List<RemoteSessionRecord> all)
	{
		var result = new List<RemoteSessionRecord>(sessions.Count);
		foreach (var s in sessions)
		{
			var record = new RemoteSessionRecord(
				s, s.NodeId, s.Name, s.SupervisorNodeId, dir.NodeId, dir.Path, s.WorkingDirectory,
				s.ExternalId ?? s.ClaudeSessionId, s.IsFavorite, s.RecentIgnoredAt);
			result.Add(record);
			all.Add(record);
		}
		return result;
	}
}
