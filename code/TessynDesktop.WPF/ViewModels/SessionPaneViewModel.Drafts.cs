using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The unsent text of a session lives in the daemon (<c>draft.get/set</c>, W1b): it is written a moment after the user stops typing, read when a
/// session opens, merged with what another client wrote (<c>draft.changed</c>, a conflicting write) and shown in the tree through the daemon's
/// <c>hasDraft</c> state. <see cref="InputText"/> stays the source of truth while the session is open; <c>_drafts</c> only holds the text of
/// sessions the pane left.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private bool _applyingDraft;

	/// <summary>Writes drafts to the daemon; the app flushes it at exit.</summary>
	public DraftSynchronizer DraftSync { get; private set; } = null!;

	private void InitializeDrafts()
	{
		DraftSync = new DraftSynchronizer(_client, _logger, Constants.ComposerLimits.DraftSaveDelay);
		DraftSync.Merged += OnDraftMerged;
	}

	partial void OnInputTextChanged(string value)
	{
		if (!_applyingDraft && NodeId != null)
			DraftSync.Schedule(NodeId, value);

		OnInputChangedForAutocomplete();
	}

	/// <summary>Shows text in the input without saving it as a draft (it came from the daemon or the local cache).</summary>
	private void SetInputQuietly(string text)
	{
		_applyingDraft = true;
		try
		{
			SetInput(text);
		}
		finally
		{
			_applyingDraft = false;
		}
	}

	/// <summary>Changes the text of a session the pane has left (a finished file copy, a send that did not happen) and saves it as that session's draft.</summary>
	private void SetDraftText(string nodeId, string text)
	{
		_drafts[nodeId] = text;
		DraftSync.Schedule(nodeId, text);
	}

	/// <summary>The pane is leaving a session: its text is kept for a quick return and written at once.</summary>
	private void LeaveDraft(string nodeId, string text)
	{
		_drafts[nodeId] = text;
		_ = DraftSync.FlushAsync(nodeId);
	}

	/// <summary>Reads the daemon's draft of the session just opened (or after a resync) and merges it with what is in the box.</summary>
	private async Task ReconcileDraftAsync(string nodeId)
	{
		DraftLoad load;
		try
		{
			load = await DraftSync.LoadAsync(nodeId).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the draft of {NodeId} failed", nodeId);
			return;
		}

		_ui.Post(() => ApplyRemoteDraft(nodeId, load));
	}

	private void ApplyRemoteDraft(string nodeId, DraftLoad load)
	{
		if (nodeId != NodeId)
		{
			// M3: an answer issued before the pane left the session must not put the old daemon text over what was typed and saved since.
			var cached = _drafts.GetValueOrDefault(nodeId);
			if (!DraftSync.HasPending(nodeId) && (string.IsNullOrEmpty(cached) || cached == load.PreviousBase))
				_drafts[nodeId] = load.Text;

			return;
		}

		var local = InputText;
		if (local == load.Text)
			return;

		// Nothing typed since the last agreement: the other writer's text simply replaces it. Otherwise both are kept.
		var dirty = load.PreviousBase == null ? local.Length > 0 : local != load.PreviousBase;
		if (!dirty)
		{
			DraftSync.Discard(nodeId);   // M1: a write still waiting would put the older text back over the one just read
			SetInputQuietly(load.Text);
			InputCaretRequested?.Invoke(this, load.Text.Length);
			return;
		}

		var merged = DraftMerge.Merge(local, load.Text, load.PreviousBase);
		if (merged != local)
		{
			SetInput(merged);   // saved with the revision just read
			InputCaretRequested?.Invoke(this, merged.Length);
		}
	}

	private void OnDraftChanged(DraftChangedPayload payload)
	{
		// FR.23.19: the reply draft of a Focus item has its own revision and is not the composer text; it must not trigger a reconcile or drop the cache.
		if (payload.ItemId != null)
			return;

		if (DraftSync.IsKnownRevision(payload.NodeId, payload.Revision))
			return;   // our own write

		if (payload.NodeId == NodeId)
		{
			_ = ReconcileDraftAsync(payload.NodeId);
			return;
		}

		if (!DraftSync.HasPending(payload.NodeId))
		{
			_drafts.Remove(payload.NodeId);   // read again when the session is opened
			DraftSync.Forget(payload.NodeId);
		}
	}

	/// <summary>A conflicting write was merged with the daemon's newer text: the box shows the merge unless the user typed on since.</summary>
	private void OnDraftMerged(object? sender, DraftMerged merged) => _ui.Post(() =>
	{
		if (merged.NodeId == NodeId)
		{
			if (InputText == merged.Sent)
			{
				SetInputQuietly(merged.Merged);
				InputCaretRequested?.Invoke(this, merged.Merged.Length);
			}
			else
			{
				// M2: the user typed on; their text and the merged one are merged again, so the text still waiting cannot replace the daemon's.
				// Text typed at the end of what was sent follows the merge; anything else is merged as two edits.
				var again = InputText.StartsWith(merged.Sent, StringComparison.Ordinal) && merged.Sent.Length > 0
					? merged.Merged + InputText[merged.Sent.Length..]
					: DraftMerge.Merge(InputText, merged.Merged, merged.Sent);
				if (again != InputText)
				{
					SetInput(again);
					InputCaretRequested?.Invoke(this, again.Length);
				}
			}
		}
		else if (_drafts.GetValueOrDefault(merged.NodeId) == merged.Sent)
		{
			_drafts[merged.NodeId] = merged.Merged;
		}
	});

	/// <summary>After a resync draft events may have been lost: the open session's draft is read again and other cached texts are dropped.</summary>
	private void ResyncDrafts(string? openNodeId)
	{
		foreach (var nodeId in _drafts.Keys.Where(k => k != openNodeId && !DraftSync.HasPending(k)).ToList())
		{
			_drafts.Remove(nodeId);
			DraftSync.Forget(nodeId);
		}

		_ = DraftSync.FlushAllAsync();   // M4: what a lost connection kept from being written goes out now
		if (openNodeId != null)
			_ = ReconcileDraftAsync(openNodeId);
	}
}
