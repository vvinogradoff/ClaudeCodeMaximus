namespace TessynDesktop.WPF.ViewModels;

/// <summary>One directory's worth of rows for <see cref="GroupedSessionListViewModel.Show"/>; the rows are in display order.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionGroupData(string DirectoryId, string Label, IReadOnlyList<SessionRowViewModel> Rows);
