using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>The collapsible directory header of a grouped session list (FR.1.16): label, session count and a chevron.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionGroupHeaderViewModel : ObservableObject
{
	[ObservableProperty]
	private string _label;

	[ObservableProperty]
	private int _count;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(Chevron))]
	private bool _isExpanded = true;

	public SessionGroupHeaderViewModel(string directoryId, string label, int count)
	{
		DirectoryId = directoryId;
		_label = label;
		_count = count;
	}

	public string DirectoryId { get; }

	/// <summary>Expanded or collapsed, as the Avalonia header draws it.</summary>
	public string Chevron => IsExpanded ? Constants.SessionLists.ChevronExpanded : Constants.SessionLists.ChevronCollapsed;

	public string AutomationName => Constants.SessionLists.GroupPrefix + Label;

	/// <summary>Toggles the group; the list that owns the header sets it (parameter: the header).</summary>
	public ICommand? ToggleCommand { get; set; }
}
