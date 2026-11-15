using System.Collections.ObjectModel;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>The sessions of one directory in the Tags tab's list.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagGroupViewModel
{
	public TagGroupViewModel(string directoryId, string directoryLabel, IEnumerable<SessionRowViewModel> sessions)
	{
		DirectoryId = directoryId;
		DirectoryLabel = directoryLabel;
		Sessions = new ObservableCollection<SessionRowViewModel>(sessions);
	}

	public string DirectoryId { get; }

	public string DirectoryLabel { get; }

	public ObservableCollection<SessionRowViewModel> Sessions { get; }
}
