using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using TessynDesktop.Services;

namespace TessynDesktop.ViewModels;

/// <summary>
/// Builds the directory-grouped session lists of the Recent tab (FR.1.16) and the Tags tab (FR.24.12):
/// top-level sessions only, directories in tree order, drafts first, then newest Last Activity first.
/// </summary>
/// <remarks>Created by Claude</remarks>
public static class SessionGroupListBuilder
{
	/// <param name="include">Optional filter on top-level sessions; null includes every one.</param>
	public static List<RecentSessionGroupViewModel> Build(
		IEnumerable<DirectoryNodeViewModel> directories,
		IDirectoryLabelService labelService,
		Func<SessionNodeViewModel, bool>? include = null)
	{
		var groups = new List<RecentSessionGroupViewModel>();

		foreach (var dir in directories)
		{
			var sessions = new List<SessionNodeViewModel>();
			CollectTopLevelSessions(dir.Children, sessions);

			if (include is not null)
				sessions = sessions.Where(include).ToList();

			if (sessions.Count == 0)
				continue;

			var group = new RecentSessionGroupViewModel(labelService.GetLabel(dir.Path), dir.Path);

			foreach (var s in sessions
				.OrderByDescending(s => s.HasDraftText)
				.ThenByDescending(s => s.LastActivityTimestamp ?? DateTimeOffset.MinValue))
			{
				group.Sessions.Add(s);
			}

			groups.Add(group);
		}

		return groups;
	}

	/// <summary>Collects sessions directly under a Directory node or inside its Groups; Worker Sessions stay nested.</summary>
	public static void CollectTopLevelSessions(ObservableCollection<ViewModelBase> children, List<SessionNodeViewModel> result)
	{
		foreach (var child in children)
		{
			if (child is SessionNodeViewModel session)
				result.Add(session);
			else if (child is GroupNodeViewModel group)
				CollectTopLevelSessions(group.Children, result);
		}
	}
}
