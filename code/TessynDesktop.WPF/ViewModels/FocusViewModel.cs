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
/// nothing here depends on the transcript.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FocusViewModel : ObservableObject, IDisposable
{
	private readonly ITessynClient _client;
	private readonly IUiDispatcher _ui;
	private readonly ILogger<FocusViewModel> _logger;
	private readonly Dictionary<(string NodeId, string ItemId), string> _drafts = new();
	private readonly Dictionary<string, FocusItemViewModel> _byId = new(StringComparer.OrdinalIgnoreCase);

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
		_client.NotificationReceived += OnNotification;
		_client.Resynced += OnResynced;
	}

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

			var item = new FocusItemViewModel(dto, this);
			if (_drafts.Remove((nodeId, dto.Id), out var draft))
				item.ReplyText = draft;
			_byId[dto.Id] = item;
			wanted.Add(item);
		}

		// Sync in place (no Clear): a card the user is typing into keeps its focus across refreshes.
		foreach (var gone in Items.Where(i => !wanted.Contains(i)).ToList())
		{
			Items.Remove(gone);
			_byId.Remove(gone.Id);
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
		}
	}

	private void RaiseCountsChanged()
	{
		AnswerCount = Items.Count(i => i.HasReply);
		OnPropertyChanged(nameof(HasItems));
		OnPropertyChanged(nameof(IsEmpty));
		SendAnswersCommand.NotifyCanExecuteChanged();
	}

	partial void OnAnswerCountChanged(int value) => OnPropertyChanged(nameof(SendAnswersText));

	/// <summary>A reply box changed: the "Send answers" count follows.</summary>
	internal void OnReplyTextChanged()
	{
		AnswerCount = Items.Count(i => i.HasReply);
		SendAnswersCommand.NotifyCanExecuteChanged();
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
				card.ReplyText = string.Empty;

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

	private void OnResynced(object? sender, ResyncedEventArgs e) => _ui.Post(() =>
	{
		if (NodeId != null)
			_ = RequestRefresh();
	});

	public void Dispose()
	{
		_client.NotificationReceived -= OnNotification;
		_client.Resynced -= OnResynced;
	}
}
