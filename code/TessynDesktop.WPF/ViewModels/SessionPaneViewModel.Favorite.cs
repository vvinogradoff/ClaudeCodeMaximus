using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>The favourite toggle of the session header (FR.9.7): shows the open session's flag and flips it through <c>session.setFavorite</c>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	/// <summary>The open session is a favourite.</summary>
	[ObservableProperty]
	private bool _isFavorite;

	private void ApplyFavorite(SessionStateDto state) => IsFavorite = state.Favorite;

	[RelayCommand]
	private async Task ToggleFavoriteAsync()
	{
		var nodeId = NodeId;
		if (nodeId == null || !HasSession)
			return;

		try
		{
			var result = await _client.SessionSetFavoriteAsync(nodeId, !IsFavorite);
			if (nodeId == NodeId)
				IsFavorite = result.Value;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.LogWarning(ex, "session.setFavorite failed for {NodeId}", nodeId);
			Notice = $"The favourite flag could not be changed: {ex.Message}";
		}
	}
}
