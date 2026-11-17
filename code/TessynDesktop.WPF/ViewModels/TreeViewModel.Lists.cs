using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>The read side the Recent and Tags tabs need from the tree (W3): the top-level sessions per directory, and the directory of a node.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TreeViewModel
{
	/// <summary>Raised on the UI thread after the tree was rebuilt or a session's state changed.</summary>
	public event EventHandler? ListsChanged;

	private void RaiseListsChanged() => ListsChanged?.Invoke(this, EventArgs.Empty);

	/// <summary>Re-reads the recency tier and the dotted border of every row (called every 60 seconds).</summary>
	public void RefreshClock()
	{
		foreach (var node in _byId.Values)
			node.RefreshClock();
	}

	/// <summary>Re-reads the date text of every row (the date format setting changed).</summary>
	public void RefreshDisplay()
	{
		foreach (var node in _byId.Values)
			node.RefreshDisplay();
	}

	/// <summary>
	/// The top-level sessions (directly under a directory or inside its groups; workers stay nested) of every directory, directories in tree
	/// order, sessions in tree order. The data is the tree snapshot plus the pushed states: no extra call.
	/// </summary>
	public IReadOnlyList<TopLevelSessionGroup> TopLevelSessions()
	{
		var result = new List<TopLevelSessionGroup>();
		foreach (var directory in _roots.Where(r => r.Kind == TreeNodeKind.Directory))
		{
			var sessions = new List<TreeNodeViewModel>();
			Collect(directory, sessions);
			if (sessions.Count > 0)
				result.Add(new TopLevelSessionGroup(directory.Id, directory.Label, sessions));
		}

		return result;
	}

	private static void Collect(TreeNodeViewModel parent, List<TreeNodeViewModel> into)
	{
		foreach (var child in parent.Children)
		{
			if (child.Kind == TreeNodeKind.Session)
				into.Add(child);
			else if (child.Kind == TreeNodeKind.Group)
				Collect(child, into);
		}
	}

	/// <summary>The id of the directory a node lives in (a worker lives in its supervisor's directory); null for an unknown node.</summary>
	public string? DirectoryIdOf(string nodeId)
	{
		if (!_byId.TryGetValue(nodeId, out var node))
			return null;

		while (node.Parent != null)
			node = node.Parent;

		return node.Kind == TreeNodeKind.Directory ? node.Id : null;
	}

	/// <summary>
	/// The session id to send as <c>scopeNodeId</c>: the selected session or worker (the daemon scopes a worker like its supervisor). Null with no selection and for a selected
	/// directory or group, as in the Avalonia client and FR.1.10 / FR.24.11 (the project scope belongs to a selected session; with a directory selected the scope is off and
	/// every tag is searched).
	/// </summary>
	public string? ScopeSessionIdOfSelection() => SelectedNode is { IsSessionLike: true } node ? node.Id : null;
}
