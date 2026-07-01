using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ClaudeMaximus.Services;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <summary>
/// Flat list of all sessions grouped by project directory.
/// Within each group: sessions with drafts first (newest→oldest), then remaining (newest→oldest).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentSessionsViewModel : ViewModelBase
{
	private readonly SessionTreeViewModel _sessionTree;
	private readonly IDirectoryLabelService _labelService;
	private SessionNodeViewModel? _selectedSession;

	public ObservableCollection<RecentSessionGroupViewModel> Groups { get; } = [];

	public SessionNodeViewModel? SelectedSession
	{
		get => _selectedSession;
		set => this.RaiseAndSetIfChanged(ref _selectedSession, value);
	}

	public RecentSessionsViewModel(SessionTreeViewModel sessionTree, IDirectoryLabelService labelService)
	{
		_sessionTree = sessionTree;
		_labelService = labelService;
	}

	/// <summary>Rebuilds the flat list from the current tree state.</summary>
	public void Refresh()
	{
		Groups.Clear();

		foreach (var dir in _sessionTree.Directories)
		{
			var sessions = new List<SessionNodeViewModel>();
			CollectSessions(dir.Children, sessions);

			if (sessions.Count == 0)
				continue;

			// Drafts first (newest→oldest), then remaining (newest→oldest)
			var drafts = sessions
				.Where(s => s.HasDraftText)
				.OrderByDescending(s => s.LastPromptTimestamp ?? DateTimeOffset.MinValue)
				.ToList();

			var rest = sessions
				.Where(s => !s.HasDraftText)
				.OrderByDescending(s => s.LastPromptTimestamp ?? DateTimeOffset.MinValue)
				.ToList();

			var group = new RecentSessionGroupViewModel(
				_labelService.GetLabel(dir.Path),
				dir.Path);

			foreach (var s in drafts)
				group.Sessions.Add(s);
			foreach (var s in rest)
				group.Sessions.Add(s);

			Groups.Add(group);
		}
	}

	private static void CollectSessions(ObservableCollection<ViewModelBase> children, List<SessionNodeViewModel> result)
	{
		foreach (var child in children)
		{
			if (child is SessionNodeViewModel session)
				result.Add(session);
			else if (child is GroupNodeViewModel group)
				CollectSessions(group.Children, result);
		}
	}
}

/// <summary>A directory group header in the recent sessions flat list.</summary>
public sealed class RecentSessionGroupViewModel : ViewModelBase
{
	public string Label { get; }
	public string Path { get; }
	public ObservableCollection<SessionNodeViewModel> Sessions { get; } = [];

	public RecentSessionGroupViewModel(string label, string path)
	{
		Label = label;
		Path = path;
	}
}
