using System.Collections.ObjectModel;
using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using ReactiveUI;

namespace ClaudeMaximus.ViewModels;

/// <remarks>Created by Claude</remarks>
public sealed class DirectoryNodeViewModel : ViewModelBase
{
	private readonly IDirectoryLabelService _labelService;
	private bool _isVisible = true;
	private bool _isExpanded;

	public DirectoryNodeModel Model { get; }

	public string Label => _labelService.GetLabel(Model.Path);
	public string Path => Model.Path;

	/// <summary>
	/// Combined children collection for TreeView binding.
	/// Contains GroupNodeViewModel and SessionNodeViewModel in display order (groups first).
	/// </summary>
	public ObservableCollection<ViewModelBase> Children { get; } = [];

	/// <summary>Bound to TreeViewItem.IsExpanded for persist/restore.</summary>
	public bool IsExpanded
	{
		get => _isExpanded;
		set
		{
			this.RaiseAndSetIfChanged(ref _isExpanded, value);
			Model.IsExpanded = value;
		}
	}

	public DirectoryNodeViewModel(DirectoryNodeModel model, IDirectoryLabelService labelService)
	{
		Model = model;
		_labelService = labelService;
		_isExpanded = model.IsExpanded;

		foreach (var g in model.Groups)
			Children.Add(new GroupNodeViewModel(g));

		foreach (var s in model.Sessions)
			Children.Add(new SessionNodeViewModel(s));
	}

	public void AddGroup(GroupNodeViewModel group)
	{
		Children.Add(group);
		Model.Groups.Add(group.Model);
	}

	public void AddSession(SessionNodeViewModel session)
	{
		Children.Add(session);
		Model.Sessions.Add(session.Model);
	}

	/// <summary>Controls visibility during search filtering.</summary>
	public bool IsVisible
	{
		get => _isVisible;
		set => this.RaiseAndSetIfChanged(ref _isVisible, value);
	}

	public bool CanDelete => Children.Count == 0;
}
