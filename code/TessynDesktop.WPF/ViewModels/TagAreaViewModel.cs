using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The session tag area (FR.24): the chips and whether the area is "mini" (one line) or "full" (up to ten lines). The mode is never remembered:
/// the area is collapsed by default, and <see cref="SessionPaneViewModel"/> collapses it whenever another session is opened. Switching between
/// the Session and Focus views leaves it alone.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TagAreaViewModel : ObservableObject
{
	[ObservableProperty]
	private bool _isExpanded;

	public TagAreaViewModel(ObservableCollection<SessionTagViewModel> tags) => Tags = tags;

	public ObservableCollection<SessionTagViewModel> Tags { get; }

	[RelayCommand]
	private void Toggle() => IsExpanded = !IsExpanded;
}
