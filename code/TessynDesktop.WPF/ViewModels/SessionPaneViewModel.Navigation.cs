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

	/// <summary>The side pane has something to show: a file preview (the Focus view has its own place, FR.23.12).</summary>
	public bool HasSideContent => Preview?.IsOpen == true;

	public bool IsSidePaneVisible => HasSideContent && !SideCollapsed;

	/// <summary>Number shown on the Focus toggle: open items other than Decided.</summary>
	public int FocusBadge => Focus.OpenCount;

	/// <summary>Reads the optional media-preview allowlist from the daemon (FR.26.4); safe to call when the daemon does not send one.</summary>
	public Task InitializeAsync() => Task.WhenAll(_media?.RefreshAsync(_client) ?? Task.CompletedTask, LoadCatalogAsync());

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
			OnPropertyChanged(nameof(IsFocusBadgeVisible));
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
		var openToken = _openToken;
		try
		{
			if (origin.NodeId == NodeId && _current != null)
			{
				if (origin.AtBottom)
				{
					if (_current.HasNewer)
					{
						await ReloadCoreAsync(_openToken);   // the view came from a jump and stops short of the newest entries
						return;
					}

					_viewAtBottom = true;
					_surface.Load(origin.NodeId, _current.Views, _current.HasOlder, toBottom: true, hasNewer: false);
					PushLive();   // a load clears the page's live row
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

			if (openToken != _openToken)
				return;   // a plain selection (tree click) happened meanwhile: it wins

			await OpenCoreAsync(origin.NodeId, null, target, page, shown: () => Navigated?.Invoke(this, origin.NodeId));
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
		var openToken = _openToken;
		try
		{
			// Already loaded in this session: just scroll.
			if (nodeId == NodeId && _current != null)
			{
				var loaded = _current.Entries.FirstOrDefault(e => e.EntryId == entryId);
				if (loaded != null)
				{
					PushBack(origin);
					IsFocusSelected = false;   // the entry is in the transcript: show it (FR.23.12)
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

			if (openToken != _openToken)
				return;   // a plain selection (tree click) happened while the page was loading: it wins, nothing is pushed or announced

			PushBack(origin); // only now: when the entry is gone nothing changed and there is nothing to go back to
			var shown = await OpenCoreAsync(nodeId, null, new JumpTarget(entryId, reference.Offset, reference.Length, Highlight: true), page,
				shown: () => Navigated?.Invoke(this, nodeId));   // announced right after the page got the entries, only if this open is still current
			if (!shown && origin != null && _back.Count > 0 && ReferenceEquals(_back[^1], origin))
			{
				_back.RemoveAt(_back.Count - 1);   // the open did not happen: there is nothing to go back from
				NotifyBack();
			}
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

	private readonly Dictionary<string, string> _inlineImages = new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _inlineImageSlots = new(3);

	private void OnPathImageRequested(object? sender, PathClickedEventArgs e) => _ui.Post(() => _ = ShowPathImageAsync(e.EntryIndex, e.Path));

	/// <summary>
	/// The page shows an image path inline and asks for the image (FR.26.5): the same daemon check as a click (the path must occur in that entry, and
	/// the file-access rules apply), then the image is copied into the preview cache and its URL goes back to the page. A refusal by the access rules
	/// answers "not allowed"; a missing file or any other failure answers "unavailable".
	/// </summary>
	public async Task ShowPathImageAsync(int entryIndex, string path)
	{
		var nodeId = NodeId;
		var entry = _current?.Entries.FirstOrDefault(e => e.Index == entryIndex);
		if (nodeId == null || Preview == null || entry?.EntryId == null || !_pathDetector.IsPathShaped(path))
		{
			_surface.ShowPathImage(entryIndex, path, null, denied: false);
			return;
		}

		var key = nodeId + "|" + entry.EntryId + "|" + path;
		if (_inlineImages.TryGetValue(key, out var known))
		{
			_surface.ShowPathImage(entryIndex, path, known, denied: false);
			return;
		}

		FilePreviewResult result;
		try
		{
			result = await _client.FilePreviewAsync(nodeId, entry.EntryId, path);
		}
		catch (Exception ex)
		{
			_logger.LogInformation(ex, "file.preview refused an inline image of {NodeId}", nodeId);
			if (NodeId == nodeId)
				_surface.ShowPathImage(entryIndex, path, null, denied: ex.Message.Contains(Constants.Texts.FileAccessRefusalFragment, StringComparison.OrdinalIgnoreCase));
			return;
		}

		string? src = null;
		await _inlineImageSlots.WaitAsync();
		try
		{
			src = await Preview.CacheImageAsync(result.Handle, CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.LogInformation(ex, "An inline image of {NodeId} could not be read", nodeId);
		}
		finally
		{
			_inlineImageSlots.Release();
		}

		if (src != null)
			_inlineImages[key] = src;

		if (NodeId == nodeId)
			_surface.ShowPathImage(entryIndex, path, src, denied: false);
	}

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
