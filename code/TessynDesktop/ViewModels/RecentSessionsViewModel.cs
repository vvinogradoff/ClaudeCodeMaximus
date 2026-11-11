using System;
using System.Collections.ObjectModel;
using TessynDesktop.Services;

namespace TessynDesktop.ViewModels;

/// <summary>
/// Flat list of all top-level sessions grouped by project directory (FR.1.16).
/// Within each group: sessions with drafts first (newest→oldest), then remaining (newest→oldest).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentSessionsViewModel : ViewModelBase, ISessionGroupListHost
{
	private readonly SessionTreeViewModel _sessionTree;
	private readonly IDirectoryLabelService _labelService;

	public ObservableCollection<RecentSessionGroupViewModel> Groups { get; } = [];

	/// <summary>
	/// Raised on every row click (FR.1.16), including a repeat click on the previously clicked row, which a
	/// change-tracked property would swallow.
	/// </summary>
	public event Action<SessionNodeViewModel>? SessionChosen;

	public RecentSessionsViewModel(SessionTreeViewModel sessionTree, IDirectoryLabelService labelService)
	{
		_sessionTree = sessionTree;
		_labelService = labelService;
	}

	public void SelectSession(SessionNodeViewModel session) => SessionChosen?.Invoke(session);

	/// <summary>
	/// Rebuilds the list from the current tree state (FR.1.16). Each group is expanded iff
	/// <see cref="RecentSessionVisibilityService.IsGroupExpandedOnRecentTabSwitch"/> says so for the selected
	/// session's Directory node (null = no selected session = everything expanded).
	/// </summary>
	public void Refresh(string? selectedSessionDirectoryPath)
	{
		Groups.Clear();

		foreach (var group in SessionGroupListBuilder.Build(_sessionTree.Directories, _labelService))
		{
			group.IsExpanded = RecentSessionVisibilityService.IsGroupExpandedOnRecentTabSwitch(group.Path, selectedSessionDirectoryPath);
			Groups.Add(group);
		}
	}
}
