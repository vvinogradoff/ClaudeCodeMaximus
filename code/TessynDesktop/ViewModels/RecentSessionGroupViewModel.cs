using System.Collections.ObjectModel;
using ReactiveUI;

namespace TessynDesktop.ViewModels;

/// <summary>
/// A collapsible directory group header with its sessions, shared by the Recent tab (FR.1.16) and the
/// Tags tab session list (FR.24.12).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentSessionGroupViewModel : ViewModelBase
{
	private bool _isExpanded = true;

	public string Label { get; }
	public string Path { get; }
	public ObservableCollection<SessionNodeViewModel> Sessions { get; } = [];

	/// <summary>Whether the sessions are shown (FR.1.16). Defaults to true; toggled by clicking the header.</summary>
	public bool IsExpanded
	{
		get => _isExpanded;
		set => this.RaiseAndSetIfChanged(ref _isExpanded, value);
	}

	public RecentSessionGroupViewModel(string label, string path)
	{
		Label = label;
		Path = path;
	}
}
