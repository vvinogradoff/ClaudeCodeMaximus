using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// Renaming the open session in place: a double click on its title in the session header turns the title into a text box (Enter accepts, Esc cancels). The new name goes
/// to the daemon with <c>tree.rename</c>; the tree follows through the daemon's <c>tree.changed</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	/// <summary>The title is being edited.</summary>
	[ObservableProperty]
	private bool _isRenaming;

	/// <summary>The text of the title box while <see cref="IsRenaming"/>.</summary>
	[ObservableProperty]
	private string _renameText = string.Empty;

	/// <summary>Replaces the title with a text box holding the current name.</summary>
	[RelayCommand]
	private void BeginRename()
	{
		if (!HasSession || NodeId == null)
			return;

		RenameText = Title;
		IsRenaming = true;
	}

	/// <summary>Esc: back to the title, the name unchanged.</summary>
	[RelayCommand]
	private void CancelRename() => IsRenaming = false;

	/// <summary>Enter: renames the session. An empty or unchanged name only closes the box; a refusal of the daemon shows as a notice and keeps the old title.</summary>
	[RelayCommand]
	private async Task CommitRenameAsync()
	{
		var nodeId = NodeId;
		var name = RenameText.Trim();
		IsRenaming = false;
		if (nodeId == null || !HasSession || name.Length == 0 || name == Title)
			return;

		try
		{
			await _client.TreeRenameNodeAsync(nodeId, name);
			if (nodeId == NodeId)
				Title = name;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "tree.rename failed for {NodeId}", nodeId);
			Notice = $"{Constants.TreeTexts.RenameFailed}: {ex.Message}";
		}
	}
}
