using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The Focus view of the selected session (FR.23): its open Attention Items, newest first, with answering ("Send answers", per-card
/// Reply) and dismissing through the Remote API (<c>attention.list</c>, <c>attention.reply</c>, <c>attention.dismiss</c>). It follows
/// <c>attention.changed</c> events of its own session and reloads after a client resync. References on items are handed to the owner
/// (the session pane) through <see cref="RefActivated"/>. It serves one session at a time and leaves room for a cross-session list:
/// nothing here depends on the transcript. The text of every reply box is a draft kept by the daemon (FR.23.19): written through <c>draft.set</c> with the
/// item id a moment after typing stops (the <see cref="DraftSynchronizer"/> of the composer, over the reply endpoint), read again when a card shows, merged with
/// what another client wrote, and dropped when the reply is sent or the item closes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FocusViewModel : ObservableObject, IDisposable
{
	private readonly ITessynClient _client;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<FocusViewModel> _logger;
	private readonly Dictionary<(string NodeId, string ItemId), string> _drafts = new();
	private readonly Dictionary<string, FocusItemViewModel> _byId = new(StringComparer.OrdinalIgnoreCase);
	private bool _applyingReply;

	private int _token;
	private Task _refreshTask = Task.CompletedTask;
	private bool _refreshRunning;
	private bool _refreshPending;

	[ObservableProperty]
	private string? _nodeId;

	[ObservableProperty]
	private string? _error;

	[ObservableProperty]
	private int _openCount;

	[ObservableProperty]
	private int _answerCount;

	public FocusViewModel(ITessynClient client, IUiDispatcher ui, ILogger<FocusViewModel> logger)
	{
		_client = client;
		_ui = ui;
		_logger = logger;
		ReplySync = new DraftSynchronizer(
			(key, ct) => _client.ReplyDraftGetAsync(NodeOf(key), ItemOf(key), ct),
			(key, text, baseRevision) => _client.ReplyDraftSetAsync(NodeOf(key), ItemOf(key), text, baseRevision),
			logger, Constants.ComposerLimits.DraftSaveDelay);
		ReplySync.Merged += OnReplyMerged;
		_client.NotificationReceived += OnNotification;
		_client.Resynced += OnResynced;
	}

	/// <summary>Writes the reply drafts to the daemon; the app flushes it at exit. Its keys join a node id and an item id.</summary>
	public DraftSynchronizer ReplySync { get; }

	private const char KeySeparator = '\n';

	public static string KeyOf(string nodeId, string itemId) => nodeId + KeySeparator + itemId;

	private static string NodeOf(string key) => key[..key.IndexOf(KeySeparator)];

	private static string ItemOf(string key) => key[(key.IndexOf(KeySeparator) + 1)..];

	/// <summary>The open items of the session, newest first.</summary>
	public ObservableCollection<FocusItemViewModel> Items { get; } = [];

	/// <summary>A reference of an item was activated (the argument is the reference); the owner jumps, previews or opens it.</summary>
	public event EventHandler<AttentionRefDto>? RefActivated;

	public bool HasItems => Items.Count > 0;

	/// <summary>True when there is nothing open: the view shows <see cref="Constants.Texts.NoOpenItems"/>.</summary>
	public bool IsEmpty => Items.Count == 0;

	public string SendAnswersText => AnswerCount > 0 ? $"Send answers ({AnswerCount})" : "Send answers";

	/// <summary>Completes when no refresh is running.</summary>
	public Task WhenIdleAsync() => _refreshTask;

	/// <summary>Shows the items of a session.</summary>
	public Task SetSessionAsync(string nodeId)
	{
		if (nodeId == NodeId)
			return RequestRefresh();

		SaveDrafts();
		_token++;
		NodeId = nodeId;
		Error = null;
		ClearItems();
		return RequestRefresh();
	}

	/// <summary>Stops showing a session.</summary>
	public void Clear()
	{
		SaveDrafts();
		_token++;
		NodeId = null;
		Error = null;
		ClearItems();
	}

	public Task RefreshAsync() => RequestRefresh();

	private Task RequestRefresh()
	{
		_refreshPending = true;
		if (!_refreshRunning)
		{
			_refreshRunning = true;
			_refreshTask = RunRefreshLoopAsync();
		}

		return _refreshTask;
	}

	private async Task RunRefreshLoopAsync()
	{
		try
		{
			while (_refreshPending)
			{
				_refreshPending = false;
				await RefreshCoreAsync(_token);
			}
		}
		finally
		{
			_refreshRunning = false;
		}
	}

	private async Task RefreshCoreAsync(int token)
	{
		var id = NodeId;
		if (id == null)
			return;

		try
		{
			var result = await _client.AttentionListAsync(id);
			if (token != _token)
				return;

			Apply(id, result.Items);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Loading the Focus items of {NodeId} failed", id);
			if (token == _token)
				Error = "Could not load the Focus items: " + ex.Message;
		}
	}

	private void Apply(string nodeId, IReadOnlyList<AttentionItemDto> dtos)
	{
		Error = null;
		var open = dtos
			.Where(d => d.Status == AttentionStatus.Open)
			.OrderByDescending(d => d.CreatedUtc)
			.ThenByDescending(d => d.Number)
			.ToList();

		var wanted = new List<FocusItemViewModel>(open.Count);
		foreach (var dto in open)
		{
			if (_byId.TryGetValue(dto.Id, out var existing))
			{
				existing.Update(dto);
				wanted.Add(existing);
				continue;
			}

			var item = new FocusItemViewModel(dto, this, nodeId);
			if (_drafts.Remove((nodeId, dto.Id), out var draft))
				SetReplyQuietly(item, draft);   // a quick return: what was typed before the pane left, until the daemon's text arrives
			_byId[dto.Id] = item;
			wanted.Add(item);
			_ = ReconcileReplyAsync(item);
		}

		// Sync in place (no Clear): a card the user is typing into keeps its focus across refreshes.
		foreach (var gone in Items.Where(i => !wanted.Contains(i)).ToList())
		{
			Items.Remove(gone);
			_byId.Remove(gone.Id);
			KeepOrDiscardGoneReply(nodeId, gone);
		}

		for (var i = 0; i < wanted.Count; i++)
		{
			if (i < Items.Count && ReferenceEquals(Items[i], wanted[i]))
				continue;

			var from = Items.IndexOf(wanted[i]);
			if (from >= 0)
				Items.Move(from, i);
			else
				Items.Insert(i, wanted[i]);
		}

		OpenCount = Items.Count(i => i.Kind != AttentionKind.Decided);
		RaiseCountsChanged();
		_logger.LogInformation("Focus: {Count} open item(s) for {NodeId}", Items.Count, nodeId);
	}

	private void ClearItems()
	{
		Items.Clear();
		_byId.Clear();
		OpenCount = 0;
		AnswerCount = 0;
		RaiseCountsChanged();
	}

	private void SaveDrafts()
	{
		if (NodeId == null)
			return;

		foreach (var item in Items)
		{
			if (!string.IsNullOrWhiteSpace(item.ReplyText))
				_drafts[(NodeId, item.Id)] = item.ReplyText;

			_ = ReplySync.FlushAsync(KeyOf(NodeId, item.Id));   // leaving the session: what is still waiting goes to the daemon now
		}
	}

	/// <summary>
	/// A card left the list. Unsent text is never dropped for that: the list may be short for a moment (an unreadable ledger, a failed refresh) and the card may come back.
	/// The text is written to the daemon now and kept in the local cache for the card's return; if the item really closed, the daemon refuses the write (-32602) or deletes the draft.
	/// A card without text has nothing to lose, so its waiting empty write is dropped.
	/// </summary>
	private void KeepOrDiscardGoneReply(string nodeId, FocusItemViewModel gone)
	{
		var key = KeyOf(nodeId, gone.Id);
		if (string.IsNullOrWhiteSpace(gone.ReplyText))
		{
			ReplySync.Discard(key);
			return;
		}

		_drafts[(nodeId, gone.Id)] = gone.ReplyText;
		ReplySync.Schedule(key, gone.ReplyText);
		_ = ReplySync.FlushAsync(key);
	}

	/// <summary>Writes every pending reply draft (the app calls it at exit, and after a lost connection returns).</summary>
	public Task FlushDraftsAsync() => ReplySync.FlushAllAsync();

	/// <summary>Shows reply text without saving it as a draft (it came from the daemon or the local cache, or the reply was sent).</summary>
	private void SetReplyQuietly(FocusItemViewModel item, string text)
	{
		_applyingReply = true;
		try
		{
			item.ReplyText = text;
		}
		finally
		{
			_applyingReply = false;
		}
	}

	/// <summary>Reads the daemon's reply draft of a card and merges it with what is in the box.</summary>
	private async Task ReconcileReplyAsync(FocusItemViewModel item)
	{
		DraftLoad load;
		try
		{
			load = await ReplySync.LoadAsync(KeyOf(item.NodeId, item.Id)).ConfigureAwait(false);
		}
		catch (TessynRpcException ex) when (ex.Code == RemoteProtocol.ErrorCodes.InvalidParams)
		{
			return;   // the item is closed or gone: there is no draft to read
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Reading the reply draft of {ItemId} failed", item.Id);
			return;
		}

		_ui.Post(() => ApplyRemoteReply(item, load));
	}

	private void ApplyRemoteReply(FocusItemViewModel item, DraftLoad load)
	{
		if (item.NodeId != NodeId || !_byId.TryGetValue(item.Id, out var current) || !ReferenceEquals(current, item))
			return;

		var local = item.ReplyText;
		if (local == load.Text)
			return;

		var key = KeyOf(item.NodeId, item.Id);
		var dirty = load.PreviousBase == null ? local.Length > 0 : local != load.PreviousBase;
		if (!dirty)
		{
			ReplySync.Discard(key);   // a write still waiting would put the older text back over the one just read
			SetReplyQuietly(item, load.Text);
			return;
		}

		var merged = DraftMerge.Merge(local, load.Text, load.PreviousBase);
		if (merged != local)
			item.ReplyText = merged;   // saved with the revision just read
	}

	/// <summary>A conflicting write was merged with the daemon's newer text: the box shows the merge unless the user typed on since.</summary>
	private void OnReplyMerged(object? sender, DraftMerged merged) => _ui.Post(() =>
	{
		var node = NodeOf(merged.NodeId);
		if (node != NodeId || !_byId.TryGetValue(ItemOf(merged.NodeId), out var item) || item.ReplyText != merged.Sent)
			return;

		SetReplyQuietly(item, merged.Merged);
	});

	private void RaiseCountsChanged()
	{
		AnswerCount = Items.Count(i => i.HasReply);
		OnPropertyChanged(nameof(HasItems));
		OnPropertyChanged(nameof(IsEmpty));
		SendAnswersCommand.NotifyCanExecuteChanged();
	}

	partial void OnAnswerCountChanged(int value) => OnPropertyChanged(nameof(SendAnswersText));

	/// <summary>A reply box changed: the "Send answers" count follows and the text is saved as the item's draft (unless it came from the daemon).</summary>
	internal void OnReplyTextChanged(FocusItemViewModel item)
	{
		AnswerCount = Items.Count(i => i.HasReply);
		SendAnswersCommand.NotifyCanExecuteChanged();
		if (!_applyingReply && item.NodeId == NodeId && _byId.ContainsKey(item.Id))
			ReplySync.Schedule(KeyOf(item.NodeId, item.Id), item.ReplyText);
	}

	internal void ActivateRef(FocusRefViewModel reference) => RefActivated?.Invoke(this, reference.Reference);

	private bool CanSendAnswers() => NodeId != null && AnswerCount > 0;

	/// <summary>Sends every non-empty reply box as one answer set (the daemon builds the single prompt and starts the turn).</summary>
	[RelayCommand(CanExecute = nameof(CanSendAnswers))]
	private Task SendAnswersAsync() => SendAsync(Items.Where(i => i.HasReply).ToList());

	internal Task ReplyAsync(FocusItemViewModel item) => SendAsync([item]);

	private async Task SendAsync(IReadOnlyList<FocusItemViewModel> cards)
	{
		var id = NodeId;
		if (id == null || cards.Count == 0)
			return;

		try
		{
			Error = null;
			var answers = cards.Select(c => new AttentionAnswerParams(c.Id, c.ReplyText.Trim())).ToList();
			var result = await _client.AttentionReplyAsync(id, answers);
			if (id != NodeId)
				return;

			foreach (var card in cards.Where(c => result.AnsweredIds.Contains(c.Id, StringComparer.OrdinalIgnoreCase)))
			{
				SetReplyQuietly(card, string.Empty);
				ReplySync.Discard(KeyOf(id, card.Id));   // sent: nothing to keep (the daemon deletes the stored draft when the item closes)
			}

			await RequestRefresh();
			if (result.SkippedIds.Count > 0 && id == NodeId)
				Error = "Not sent: " + string.Join(", ", result.SkippedIds) + " (already closed or blank).";
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "attention.reply failed for {NodeId}", id);
			Error = "Sending the answers failed: " + ex.Message;
		}
	}

	/// <summary>Closes an item without an answer (Dismiss / Got it).</summary>
	internal async Task DismissAsync(FocusItemViewModel item)
	{
		var id = NodeId;
		if (id == null)
			return;

		try
		{
			Error = null;
			await _client.AttentionDismissAsync(id, item.Id);
			SetReplyQuietly(item, string.Empty);   // closed: the unsent text goes with the item (the refresh that removes the card must not keep it)
			ReplySync.Discard(KeyOf(id, item.Id));
			if (id == NodeId)
				await RequestRefresh();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "attention.dismiss failed for {NodeId}", id);
			Error = "Dismissing failed: " + ex.Message;
		}
	}

	private void OnNotification(object? sender, RemoteNotification notification)
	{
		if (notification.Method == RemoteProtocol.Composer.DraftChangedEvent)
		{
			OnDraftChanged(notification);
			return;
		}

		if (notification.Method != RemoteProtocol.Events.AttentionChanged)
			return;

		var payload = notification.Params.Deserialize<AttentionChangedPayload>(TdcpJsonOptions.Default);
		if (payload != null)
			_ui.Post(() =>
			{
				if (payload.NodeId == NodeId)
					_ = RequestRefresh();
			});
	}

	/// <summary>The reply draft of an item changed (another client, or this one's own write echoing back): the card reads it again unless the revision is the one known here.</summary>
	private void OnDraftChanged(RemoteNotification notification)
	{
		if (!notification.TryGetDraftChanged(out var payload) || payload.ItemId == null)
			return;   // the composer draft is the session pane's

		_ui.Post(() =>
		{
			if (payload.NodeId != NodeId || !_byId.TryGetValue(payload.ItemId, out var item))
				return;

			var key = KeyOf(payload.NodeId, payload.ItemId);
			if (ReplySync.IsKnownRevision(key, payload.Revision))
				return;

			_ = ReconcileReplyAsync(item);
		});
	}

	private void OnResynced(object? sender, ResyncedEventArgs e) => _ui.Post(() =>
	{
		if (NodeId == null)
			return;

		_ = RequestRefresh();
		_ = ReplySync.FlushAllAsync();   // what a lost connection kept from being written goes out now
		foreach (var item in Items.ToList())
			_ = ReconcileReplyAsync(item);   // draft events may have been lost
	});

	public void Dispose()
	{
		_client.NotificationReceived -= OnNotification;
		_client.Resynced -= OnResynced;
		ReplySync.Merged -= OnReplyMerged;
		ReplySync.Dispose();
	}
}
