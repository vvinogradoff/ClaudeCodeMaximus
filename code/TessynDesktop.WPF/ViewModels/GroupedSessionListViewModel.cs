using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// A directory-grouped session list flattened for a virtualising list box (FR.1.16, FR.24.12): a header row per directory, then its rows
/// unless the group is collapsed. <see cref="Show"/> keeps the header and row objects that stay (updating them in place) and only inserts,
/// removes and moves what changed, so a rebuild on every session change does not rebuild the visuals. The collapsed groups are remembered by
/// directory id. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class GroupedSessionListViewModel
{
	private readonly ICommand _open;
	private readonly ICommand _toggleFavorite;
	private readonly ListDiff _diff = new();
	private readonly Dictionary<string, SessionGroupHeaderViewModel> _headers = new(StringComparer.Ordinal);
	private readonly Dictionary<string, SessionRowViewModel> _rows = new(StringComparer.Ordinal);
	private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
	private IReadOnlyList<SessionGroupData> _groups = [];

	public GroupedSessionListViewModel(ICommand open, ICommand toggleFavorite)
	{
		_open = open;
		_toggleFavorite = toggleFavorite;
		ToggleGroupCommand = new RelayCommand<SessionGroupHeaderViewModel>(Toggle);
	}

	/// <summary>The visible items: <see cref="SessionGroupHeaderViewModel"/> and <see cref="SessionRowViewModel"/> in display order.</summary>
	public ObservableCollection<object> Items { get; } = [];

	public ICommand ToggleGroupCommand { get; }

	public int SessionCount => _groups.Sum(g => g.Rows.Count);

	public bool IsEmpty => _groups.Count == 0;

	/// <summary>Shows these groups: rows of a session that is still listed are the same objects, updated; the rest are new.</summary>
	/// <remarks>The rows in <paramref name="groups"/> are candidates: where a row with the id exists, the existing one takes the candidate's data.</remarks>
	public void Show(IReadOnlyList<SessionGroupData> groups)
	{
		var resolved = new List<SessionGroupData>(groups.Count);
		var liveRows = new HashSet<string>(StringComparer.Ordinal);
		var liveHeaders = new HashSet<string>(StringComparer.Ordinal);
		foreach (var group in groups)
		{
			var rows = new List<SessionRowViewModel>(group.Rows.Count);
			foreach (var candidate in group.Rows)
			{
				liveRows.Add(candidate.Id);
				if (_rows.TryGetValue(candidate.Id, out var existing))
				{
					existing.Name = candidate.Name;
					existing.State = candidate.State;
					existing.Pinned = candidate.Pinned;
					existing.CanHide = candidate.CanHide;
					existing.HasSchedules = candidate.HasSchedules;
					rows.Add(existing);
				}
				else
				{
					candidate.OpenCommand = _open;
					candidate.ToggleFavoriteCommand = _toggleFavorite;
					_rows[candidate.Id] = candidate;
					rows.Add(candidate);
				}
			}

			liveHeaders.Add(group.DirectoryId);
			if (_headers.TryGetValue(group.DirectoryId, out var header))
			{
				header.Label = group.Label;
				header.Count = rows.Count;
			}
			else
			{
				header = new SessionGroupHeaderViewModel(group.DirectoryId, group.Label, rows.Count) { ToggleCommand = ToggleGroupCommand };
				_headers[group.DirectoryId] = header;
			}

			header.IsExpanded = !_collapsed.Contains(group.DirectoryId);
			resolved.Add(group with { Rows = rows });
		}

		foreach (var gone in _rows.Keys.Where(k => !liveRows.Contains(k)).ToList())
			_rows.Remove(gone);
		foreach (var gone in _headers.Keys.Where(k => !liveHeaders.Contains(k)).ToList())
			_headers.Remove(gone);

		_groups = resolved;
		Flatten();
	}

	/// <summary>Collapses every group except <paramref name="keepDirectoryId"/>; with null every group is expanded (FR.1.16).</summary>
	public void CollapseAllExcept(string? keepDirectoryId)
	{
		_collapsed.Clear();
		if (keepDirectoryId != null)
		{
			foreach (var group in _groups.Where(g => g.DirectoryId != keepDirectoryId))
				_collapsed.Add(group.DirectoryId);
		}

		foreach (var header in _headers.Values)
			header.IsExpanded = !_collapsed.Contains(header.DirectoryId);

		Flatten();
	}

	private void Toggle(SessionGroupHeaderViewModel? header)
	{
		if (header == null)
			return;

		if (!_collapsed.Remove(header.DirectoryId))
			_collapsed.Add(header.DirectoryId);

		header.IsExpanded = !_collapsed.Contains(header.DirectoryId);
		Flatten();
	}

	private void Flatten()
	{
		var wanted = new List<object>();
		foreach (var group in _groups)
		{
			wanted.Add(_headers[group.DirectoryId]);
			if (_collapsed.Contains(group.DirectoryId))
				continue;

			foreach (var row in group.Rows)
				wanted.Add(row);
		}

		_diff.Apply(Items, wanted);
	}
}
