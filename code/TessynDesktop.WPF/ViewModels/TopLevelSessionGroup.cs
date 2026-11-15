namespace TessynDesktop.WPF.ViewModels;

/// <summary>The top-level session nodes of one directory, in tree order (see <c>TreeViewModel.TopLevelSessions</c>).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record TopLevelSessionGroup(string DirectoryId, string DirectoryLabel, IReadOnlyList<TreeNodeViewModel> Sessions);
