using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// The reply drafts of one session's Focus view (FR.23.19), client-neutral: restores the text of a card, queues what the user types and writes it after
/// a pause (an empty text deletes the draft), writes everything still queued on <see cref="Dispose"/> (the close path), and discards a draft at once when the
/// reply is sent or the item closes. A client creates one per session it shows and calls it from its Focus view model.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FocusReplyDrafts : IDisposable, IFlushesDrafts
{
	private static readonly ILogger _log = Log.ForContext<FocusReplyDrafts>();

	private readonly IDraftService _drafts;
	private readonly string _sessionFileName;
	private readonly TimeSpan _debounce;

	// Texts not yet written (null = delete), by item id. Written by the timer (a pool thread) and by Dispose, so guarded.
	private readonly object _gate = new();

	// Held across a whole flush and a whole discard, so a discard can never slip between a flush taking its snapshot and writing it (that would bring a
	// deleted draft back). Lock order: _ioGate, then _gate.
	private readonly object _ioGate = new();
	private readonly Dictionary<string, string?> _pending = new(StringComparer.OrdinalIgnoreCase);
	private Timer? _timer;
	private bool _disposed;

	/// <param name="debounce">The pause in typing before a queued text is written; <see cref="Constants.DraftDebounceMilliseconds"/> by default.</param>
	public FocusReplyDrafts(IDraftService drafts, string sessionFileName, TimeSpan? debounce = null)
	{
		_drafts          = drafts;
		_sessionFileName = sessionFileName;
		_debounce        = debounce ?? TimeSpan.FromMilliseconds(Constants.DraftDebounceMilliseconds);
	}

	/// <summary>The stored reply text of an item, or null when there is none (or it cannot be read).</summary>
	public string? Load(string itemId)
	{
		try
		{
			return _drafts.LoadItemDraft(_sessionFileName, itemId);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "FocusReplyDrafts: reading the reply draft of item {ItemId} of {FileName} failed", itemId, _sessionFileName);
			return null;
		}
	}

	/// <summary>Queues the current text of an item's reply box; a null or whitespace text queues the deletion of its draft.</summary>
	public void Queue(string itemId, string? text)
	{
		lock (_gate)
		{
			if (_disposed)
				return;

			_pending[itemId] = string.IsNullOrWhiteSpace(text) ? null : text;
			_timer ??= new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
			_timer.Change(_debounce, Timeout.InfiniteTimeSpan);
		}
	}

	/// <summary>Writes the queued texts now.</summary>
	public void Flush()
	{
		lock (_ioGate)
		{
			KeyValuePair<string, string?>[] pending;
			lock (_gate)
			{
				pending = _pending.ToArray();
				_pending.Clear();
			}

			var retry = false;
			foreach (var (itemId, text) in pending)
			{
				try
				{
					if (text == null)
						_drafts.DeleteItemDraft(_sessionFileName, itemId);
					else
						_drafts.SaveItemDraft(_sessionFileName, itemId, text);
				}
				catch (Exception ex)
				{
					_log.Warning(ex, "FocusReplyDrafts: writing the reply draft of item {ItemId} of {FileName} failed", itemId, _sessionFileName);

					// A write that failed is not lost: it waits again (unless something newer was queued meanwhile, or this is the last flush).
					lock (_gate)
					{
						if (!_disposed && _pending.TryAdd(itemId, text))
							retry = true;
					}
				}
			}

			if (retry)
			{
				lock (_gate)
				{
					if (!_disposed)
						_timer?.Change(_debounce, Timeout.InfiniteTimeSpan);
				}
			}
		}
	}

	/// <inheritdoc />
	public void FlushDrafts() => Flush();

	/// <summary>
	/// Deletes the stored drafts of items that are not among <paramref name="openItemIds"/> (an item that closed or vanished while the client was away).
	/// Does nothing when <paramref name="ledgerReadable"/> is false: an empty list from an unreadable ledger must not delete anything.
	/// </summary>
	public void Prune(IEnumerable<string> openItemIds, bool ledgerReadable = true)
	{
		if (!ledgerReadable)
			return;

		try
		{
			var open = openItemIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
			foreach (var id in _drafts.ListItemDraftIds(_sessionFileName).Where(id => !open.Contains(id)))
				Discard(id);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "FocusReplyDrafts: pruning the reply drafts of {FileName} failed", _sessionFileName);
		}
	}

	/// <summary>
	/// Discards the draft of an item that the ledger no longer shows as open (closed, or missing from it). Does nothing when <paramref name="ledgerReadable"/>
	/// is false: an unreadable ledger answers with no items, which must not make every card look stale or closed. A text still queued is kept and written.
	/// </summary>
	public void DiscardClosed(string itemId, bool ledgerReadable)
	{
		if (ledgerReadable)
			Discard(itemId);
	}

	/// <summary>The reply was sent or the item closed: forgets a queued text and deletes the stored draft now.</summary>
	public void Discard(string itemId)
	{
		lock (_ioGate)
		{
			lock (_gate)
				_pending.Remove(itemId);

			try
			{
				_drafts.DeleteItemDraft(_sessionFileName, itemId);
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "FocusReplyDrafts: deleting the reply draft of item {ItemId} of {FileName} failed", itemId, _sessionFileName);
			}
		}
	}

	/// <summary>Writes what is still queued and stops; later calls to <see cref="Queue"/> do nothing.</summary>
	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
				return;

			_disposed = true;
			_timer?.Dispose();
			_timer = null;
		}

		Flush();
	}
}
