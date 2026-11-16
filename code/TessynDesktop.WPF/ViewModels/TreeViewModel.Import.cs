namespace TessynDesktop.WPF.ViewModels;

/// <summary>The import picker's view of the tree (W7): the directories and groups sessions can be imported into.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TreeViewModel
{
	/// <summary>Every directory and group in tree order (a directory, then its groups below it), with the directory that owns each.</summary>
	public IReadOnlyList<ImportTargetViewModel> ImportTargets()
	{
		var targets = new List<ImportTargetViewModel>();
		foreach (var root in _roots.Where(r => r.Kind == TreeNodeKind.Directory))
			AddImportTargets(root, root, targets);

		return targets;
	}

	private static void AddImportTargets(TreeNodeViewModel directory, TreeNodeViewModel node, List<ImportTargetViewModel> targets)
	{
		targets.Add(new ImportTargetViewModel(node.Id, node.Label, node.Depth - directory.Depth, node.Kind == TreeNodeKind.Directory, directory.Id));
		foreach (var child in node.Children.Where(c => c.Kind == TreeNodeKind.Group))
			AddImportTargets(directory, child, targets);
	}
}
