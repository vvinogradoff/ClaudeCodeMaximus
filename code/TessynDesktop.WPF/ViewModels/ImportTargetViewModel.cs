namespace TessynDesktop.WPF.ViewModels;

/// <summary>A directory or group of the tree the picker can import into (a directory also names the Claude project the list is read from).</summary>
/// <param name="Id">The tree node id.</param>
/// <param name="Label">The row label.</param>
/// <param name="Depth">How deep the node lies in the tree (0 = directory).</param>
/// <param name="IsDirectory">A directory (also a source of sessions); a group is only a target.</param>
/// <param name="DirectoryId">The id of the directory that owns the node (its own id for a directory).</param>
/// <remarks>Created by Claude</remarks>
public sealed record ImportTargetViewModel(string Id, string Label, int Depth, bool IsDirectory, string DirectoryId)
{
	/// <summary>The label as a combo box shows it, indented by depth.</summary>
	public string DisplayText => new string(' ', Depth * 3) + (Depth > 0 ? "↳ " : string.Empty) + Label;
}
