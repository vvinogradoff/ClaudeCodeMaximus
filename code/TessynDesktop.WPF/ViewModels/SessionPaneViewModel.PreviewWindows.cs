using System.Collections.Specialized;
using System.ComponentModel;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The pane's part of the preview windows (FR.29.4, FR.29.7, FR.29.9, FR.29.11): it tells the <see cref="IPreviewRouter"/> which session is shown and which
/// Focus items closed, and offers it the files and addresses the user opens before the pane would show them itself.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private IPreviewRouter? _previewRouter;

	/// <summary>The preview router; null in tests and tools that have no preview windows. Set once, when the pane is created.</summary>
	public IPreviewRouter? PreviewRouter
	{
		get => _previewRouter;
		init
		{
			_previewRouter = value;
			if (value == null)
				return;

			PropertyChanged += OnPaneNodeChanged;
			Focus.ClosedItems.CollectionChanged += OnFocusClosedItemsChanged;
			_ = value.RestoreAsync(_client);   // the pane exists once the client is connected: the windows of the last run come back (FR.29.11)
		}
	}

	private void UnhookPreviewRouter()
	{
		if (_previewRouter == null)
			return;

		PropertyChanged -= OnPaneNodeChanged;
		Focus.ClosedItems.CollectionChanged -= OnFocusClosedItemsChanged;
	}

	private void OnPaneNodeChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(NodeId))
			_previewRouter?.OnSessionShown(NodeId);
	}

	private void OnFocusClosedItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		// Positive evidence only: items that appear in the closed list of the session on show. A clear or a switch never closes a window.
		if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null || NodeId == null || Focus.NodeId != NodeId)
			return;

		var ids = e.NewItems.OfType<FocusItemViewModel>().Select(i => i.Id).ToList();
		if (ids.Count > 0)
			_previewRouter?.OnFocusItemsClosed(NodeId, ids);
	}

	/// <summary>The Focus item a reference belongs to; null when none shows it.</summary>
	private string? FocusItemOf(AttentionRefDto reference) =>
		Focus.Items.FirstOrDefault(i => i.Refs.Any(r => ReferenceEquals(r.Reference, reference)))?.Id;

	/// <summary>Offers a file to its group's preview window; false when the mode is off or no group matches.</summary>
	private bool TryRouteFile(PreviewItemModel item, PreviewTarget target) =>
		_previewRouter?.TryOpenFile(item, target, _client) == true;
}
