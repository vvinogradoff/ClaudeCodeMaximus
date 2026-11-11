using System.Collections.ObjectModel;

namespace TessynDesktop.ViewModels;

/// <summary>
/// DataContext contract of <c>SessionGroupListView</c>: the groups to show and the click callback
/// (FR.1.16, FR.24.12).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionGroupListHost
{
	ObservableCollection<RecentSessionGroupViewModel> Groups { get; }

	/// <summary>A row was clicked. Must act on every click, including a repeat click on the same row.</summary>
	void SelectSession(SessionNodeViewModel session);
}
