using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.Transcript;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The side pane of the session pane (Focus view, file preview) and the navigation it drives (FR.23.18): jumping to a referenced
/// entry (of this or another session) with a client-side Back stack, previewing referenced files and local paths (FR.26.5), and
/// opening URL references through <see cref="UrlPolicy"/>. Everything goes through <see cref="ITessynClient"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private const int MaxBackEntries = 50;

	private readonly List<BackEntry> _back = [];

	/// <summary>Where the view was when a jump started: the session and the entry at the top of the view (null: the bottom).</summary>
	private sealed record BackEntry(string NodeId, string? EntryId, bool AtBottom);

	/// <summary>The entry a jump lands on and what to highlight in it.</summary>
	private sealed record JumpTarget(string EntryId, int? Offset, int? Length, bool Highlight);

	/// <summary>The Focus view of the open session.</summary>
	public FocusViewModel Focus { get; }

	/// <summary>The file preview pane; null in tests that do not need it.</summary>
	public FilePreviewViewModel? Preview { get; }

	/// <summary>A non-blocking message (a failed jump or preview); the user can dismiss it, it never stops anything.</summary>
	[ObservableProperty]
	private string? _notice;

	/// <summary>The user hid the side pane; opening a preview shows it again.</summary>
	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(IsSidePaneVisible))]
	private bool _sideCollapsed;

	/// <summary>Raised after a jump or Back opened a session (argument: its node id), so the tree can select it without reopening it.</summary>
	public event EventHandler<string>? Navigated;

	/// <summary>There is a place to go back to.</summary>
	public bool CanGoBack => _back.Count > 0;

	/// <summary>The side pane has something to show: open Focus items or a preview.</summary>
	public bool HasSideContent => Focus.HasItems || Preview?.IsOpen == true;

	public bool IsSidePaneVisible => HasSideContent && !SideCollapsed;

	/// <summary>Number shown on the Focus toggle: open items other than Decided.</summary>
	public int FocusBadge => Focus.OpenCount;

	/// <summary>Reads the optional media-preview allowlist from the daemon (FR.26.4); safe to call when the daemon does not send one.</summary>
	public Task InitializeAsync() => _media?.RefreshAsync(_client) ?? Task.CompletedTask;

	private void NotifyBack()
	{
		OnPropertyChanged(nameof(CanGoBack));
		BackCommand.NotifyCanExecuteChanged();
	}

	private void OnSideContentChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(FilePreviewViewModel.IsOpen) && Preview?.IsOpen == true)
			SideCollapsed = false;

		if (e.PropertyName is nameof(FocusViewModel.HasItems) or nameof(FocusViewModel.OpenCount) or nameof(FilePreviewViewModel.IsOpen))
		{
			OnPropertyChanged(nameof(HasSideContent));
			OnPropertyChanged(nameof(IsSidePaneVisible));
			OnPropertyChanged(nameof(FocusBadge));
		}
	}

	[RelayCommand]
	private void ToggleSide() => SideCollapsed = !SideCollapsed;

	[RelayCommand]
	private void DismissNotice() => Notice = null;

	[RelayCommand(CanExecute = nameof(CanGoBack))]
	private async Task BackAsync()
	{
		if (_back.Count == 0)
			return;

		var origin = _back[^1];
		_back.RemoveAt(_back.Count - 1);
		NotifyBack();
		_logger.LogInformation("Going back to {NodeId}", origin.NodeId);
		try
		{
			if (origin.NodeId == NodeId && _current != null)
			{
				if (origin.AtBottom)
				{
					_viewAtBottom = true;
					_surface.Load(origin.NodeId, _current.Views, _current.HasOlder, toBottom: true);
					return;
				}

				var loaded = _current.Entries.FirstOrDefault(e => e.EntryId == origin.EntryId);
				if (loaded != null)
				{
					_surface.GoToEntry(loaded.Index, null, highlight: false);
					return;
				}
			}

			SessionEntriesResult? page = null;
			JumpTarget? target = null;
			if (!origin.AtBottom && origin.EntryId != null)
			{
				page = await _client.SessionEntriesAsync(origin.NodeId, new SessionEntriesQuery(AroundEntryId: origin.EntryId, Limit: Constants.Limits.EntriesPageSize));
				if (page.Entries.Any(e => e.EntryId == origin.EntryId))
					target = new JumpTarget(origin.EntryId, null, null, Highlight: false);
				else
					page = null; // the entry is gone: show the newest page instead
			}

			await OpenCoreAsync(origin.NodeId, null, target, page);
			Navigated?.Invoke(this, origin.NodeId);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Going back failed");
			Notice = "Could not go back: " + ex.Message;
		}
	}

	private void OnRefActivated(object? sender, AttentionRefDto reference) => _ui.Post(() => _ = ActivateRefAsync(reference));

	/// <summary>Acts on a reference of a Focus item: entry → jump, file → preview, url → system browser (http/https only).</summary>
	public Task ActivateRefAsync(AttentionRefDto reference)
	{
		switch (reference.Kind)
		{
			case FocusRefViewModel.EntryKind:
				return JumpToEntryAsync(reference);
			case FocusRefViewModel.FileKind:
				if (Preview == null || string.IsNullOrEmpty(reference.RefId))
					return Task.CompletedTask;

				Notice = null;
				return Preview.OpenAsync(new PreviewTarget(reference.RefId, reference.Name ?? reference.Label ?? "file"));
			case FocusRefViewModel.UrlKind:
				if (!_launcher.TryOpen(reference.Url))
					Notice = "Only http and https links can be opened.";
				return Task.CompletedTask;
			default:
				return Task.CompletedTask;
		}
	}

	/// <summary>Opens the session at the referenced entry and highlights the quote; pushes the current place onto the Back stack.</summary>
	public async Task JumpToEntryAsync(AttentionRefDto reference)
	{
		var nodeId = reference.NodeId ?? NodeId;
		var entryId = reference.EntryId;
		if (nodeId == null || entryId == null)
		{
			Notice = Constants.Texts.EntryGone;
			return;
		}

		Notice = null;
		var origin = CaptureAnchor();
		_logger.LogInformation("Jumping to entry {EntryId} of {NodeId}", entryId, nodeId);
		try
		{
			// Already loaded in this session: just scroll.
			if (nodeId == NodeId && _current != null)
			{
				var loaded = _current.Entries.FirstOrDefault(e => e.EntryId == entryId);
				if (loaded != null)
				{
					PushBack(origin);
					_surface.GoToEntry(loaded.Index, QuoteOf(loaded, reference.Offset, reference.Length), highlight: true);
					return;
				}
			}

			var page = await _client.SessionEntriesAsync(nodeId, new SessionEntriesQuery(AroundEntryId: entryId, Limit: Constants.Limits.EntriesPageSize));
			if (page.Entries.All(e => e.EntryId != entryId))
			{
				Notice = Constants.Texts.EntryGone;
				return;
			}

			PushBack(origin); // only now: when the entry is gone nothing changed and there is nothing to go back to
			await OpenCoreAsync(nodeId, null, new JumpTarget(entryId, reference.Offset, reference.Length, Highlight: true), page);
			Navigated?.Invoke(this, nodeId);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Jumping to entry {EntryId} of {NodeId} failed", entryId, nodeId);
			Notice = "Could not open the referenced entry: " + ex.Message;
		}
	}

	private void ScrollToTarget(TranscriptModel model, JumpTarget target)
	{
		var entry = model.Entries.FirstOrDefault(e => e.EntryId == target.EntryId);
		if (entry != null)
			_surface.GoToEntry(entry.Index, QuoteOf(entry, target.Offset, target.Length), target.Highlight);
	}

	/// <summary>The quoted text of a reference: <c>offset</c>/<c>length</c> characters of the entry's content as served; null when out of range.</summary>
	private static string? QuoteOf(SessionEntryDto entry, int? offset, int? length)
	{
		if (offset is not { } start || length is not { } count || start < 0 || count <= 0 || start >= entry.Content.Length)
			return null;

		return entry.Content.Substring(start, Math.Min(count, entry.Content.Length - start));
	}

	private BackEntry? CaptureAnchor()
	{
		if (NodeId == null || _current == null)
			return null;

		string? entryId = null;
		if (!_viewAtBottom && _viewTop >= 0)
			entryId = _current.Entries.FirstOrDefault(e => e.Index == _viewTop)?.EntryId;

		return new BackEntry(NodeId, entryId, AtBottom: entryId == null);
	}

	private void PushBack(BackEntry? origin)
	{
		if (origin == null)
			return;

		_back.Add(origin);
		if (_back.Count > MaxBackEntries)
			_back.RemoveAt(0);
		NotifyBack();
	}

	private void OnViewChanged(object? sender, ViewAnchorEventArgs e) => _ui.Post(() =>
	{
		_viewTop = e.TopIndex;
		_viewAtBottom = e.AtBottom;
	});

	// ---- local-file chips (FR.26.5) ----

	private void OnPathClicked(object? sender, PathClickedEventArgs e) => _ui.Post(() => _ = PreviewLocalPathAsync(e.EntryIndex, e.Path));

	/// <summary>A path chip was clicked: ask the daemon for a handle (it checks that the path occurs in that entry), then open the preview.</summary>
	public async Task PreviewLocalPathAsync(int entryIndex, string path)
	{
		var nodeId = NodeId;
		var entry = _current?.Entries.FirstOrDefault(e => e.Index == entryIndex);
		if (nodeId == null || Preview == null)
			return;

		if (entry?.EntryId == null || !_pathDetector.IsPathShaped(path))
		{
			Notice = "This file cannot be previewed.";
			return;
		}

		try
		{
			Notice = null;
			var result = await _client.FilePreviewAsync(nodeId, entry.EntryId, path);
			await Preview.OpenAsync(new PreviewTarget(result.Handle, result.Name));
		}
		catch (Exception ex)
		{
			_logger.LogInformation(ex, "file.preview was refused for an entry of {NodeId}", nodeId);
			Notice = "Cannot preview this file: " + ex.Message;
		}
	}
}
