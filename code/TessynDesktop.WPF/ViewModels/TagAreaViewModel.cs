using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The session tag area (FR.24): the chips and whether the area is "mini" (one line) or "full" (up to ten lines). The mode is one global
/// setting of the WPF client, remembered in the client state; it is not per session.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TagAreaViewModel : ObservableObject
{
	private readonly IClientStateStore? _stateStore;

	[ObservableProperty]
	private bool _isExpanded;

	public TagAreaViewModel(ObservableCollection<SessionTagViewModel> tags, IClientStateStore? stateStore)
	{
		Tags = tags;
		_stateStore = stateStore;
		_isExpanded = stateStore?.Read(s => s.TagAreaExpanded) ?? false;
	}

	public ObservableCollection<SessionTagViewModel> Tags { get; }

	[RelayCommand]
	private void Toggle() => IsExpanded = !IsExpanded;

	partial void OnIsExpandedChanged(bool value)
	{
		if (_stateStore != null && _stateStore.Read(s => s.TagAreaExpanded) != value)
			_stateStore.Update(s => s.TagAreaExpanded = value);
	}
}
