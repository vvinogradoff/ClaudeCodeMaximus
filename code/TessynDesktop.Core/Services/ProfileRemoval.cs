using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>The outcome of <see cref="IProfileAdminService.Remove(string, int, DirectoryNodeModel?)"/>.</summary>
/// <param name="Profile">The profile that was removed.</param>
/// <param name="RemovedListIndex">Its selector index before the removal (1..N).</param>
/// <param name="SelectedIndex">The selection after the removal: Default (0) when the removed profile was selected, one less when a later one was.</param>
/// <param name="WasSelected">True when the removed profile was the selected one.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileRemoval(ClaudeProfileModel Profile, int RemovedListIndex, int SelectedIndex, bool WasSelected);
