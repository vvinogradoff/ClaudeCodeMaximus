using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Long-polls the Telegram Bot API for messages and mirrors every session's conversation into its
/// topic (FR.21). Mirrors the shape of <see cref="AgentMcpServer"/>'s own accept loop (manual Task +
/// CancellationTokenSource, not IHostedService) since Core has no hosting dependency. A daemon
/// client (FR.21.10): depends on <see cref="IDaemonClient"/> rather than
/// <see cref="ISessionRegistry"/>/<see cref="ISessionTurnService"/>/<see cref="ISessionFileService"/>
/// directly, making it a peer of the Avalonia UI on the daemon boundary. <see cref="IDirectoryLabelService"/>
/// and <see cref="IAppSettingsService"/>/<see cref="ITelegramClient"/> remain direct dependencies —
/// stateless utilities outside the daemon-owned turn/session domain.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TelegramBridgeService : ITelegramBridgeService
{
	private ILogger _log = Log.ForContext<TelegramBridgeService>();

	private readonly IAppSettingsService _appSettings;
	private readonly ITelegramClient _client;
	private readonly IDaemonClient _daemon;
	private readonly IDirectoryLabelService _labelService;
	private readonly TelegramInbox _inbox;

	// In-memory reverse lookup (thread → NodeId) rebuilt from the persisted forward map on Start().
	// The forward map (NodeId → thread) lives in AppSettingsModel and is the source of truth.
	private readonly ConcurrentDictionary<int, string> _nodeIdByThreadId = new();
	private readonly SemaphoreSlim _topicLock = new(1, 1);

	// FR.21.9 echo suppression: the ROLE=USER entry SessionTurnService.RunTurnAsync appends for a
	// TurnSource.Telegram turn is the same text the user just typed into the topic — set immediately
	// before routing a turn, consumed (and removed) by the first matching append the mirror sees.
	private readonly ConcurrentDictionary<string, string> _suppressNextUserAppend = new();

	private CancellationTokenSource? _cts;

	/// <summary>Logger override (tests capture entries with a hand-written sink); defaults to the static Serilog logger.</summary>
	public ILogger Logger { get => _log; set => _log = value; }

	/// <summary>
	/// The single wait seam for topic reconciliation (FR.21.18): the throttle pauses and the 429
	/// <c>retry_after</c> wait all go through it. Defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.
	/// </summary>
	public Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } = Task.Delay;

	/// <summary>Pause after a rename that really changed the topic name (FR.21.18). Settable so tests do not sleep for seconds.</summary>
	public int TopicRenameIntervalMs { get; set; } = Constants.Telegram.TopicRenameIntervalMs;

	/// <summary>Pause after a TOPIC_NOT_MODIFIED result (FR.21.18). Settable so tests do not sleep.</summary>
	public int TopicRenameNoOpIntervalMs { get; set; } = Constants.Telegram.TopicRenameNoOpIntervalMs;

	/// <summary>
	/// The background topic maintenance started by <see cref="Start"/>: Worker Session topic cleanup
	/// (FR.21.19) followed by topic name reconciliation (FR.21.18). Completes when the run ends or is
	/// cancelled. Never faults.
	/// </summary>
	public Task ReconciliationTask { get; private set; } = Task.CompletedTask;

	public TelegramBridgeService(
		IAppSettingsService appSettings,
		ITelegramClient client,
		IDaemonClient daemon,
		IDirectoryLabelService labelService,
		TelegramInbox? inbox = null)
	{
		_appSettings = appSettings;
		_client = client;
		_daemon = daemon;
		_labelService = labelService;
		_inbox = inbox ?? new TelegramInbox(client, TelegramInbox.DefaultRootDirectory());
	}

	public void Start()
	{
		if (!_appSettings.Settings.TelegramEnabled)
			return;
		if (_cts != null)
			return;

		_nodeIdByThreadId.Clear();
		foreach (var kv in _appSettings.Settings.TelegramTopicThreadIdsByNodeId)
			_nodeIdByThreadId[kv.Value] = kv.Key;

		_inbox.CleanupOlderThan(TimeSpan.FromDays(Constants.Telegram.InboxRetentionDays)); // FR.21.14

		_daemon.EntryAppended += OnEntryAppended;

		_cts = new CancellationTokenSource();
		var token = _cts.Token;
		_ = PollLoopAsync(token);
		// Runs never overlap: a new run waits for the previous one (including its final save). Runs never
		// fault. Worker-session topic cleanup (FR.21.19) runs first so reconciliation (FR.21.18) never
		// renames a topic that's about to be deleted.
		var previous = ReconciliationTask;
		ReconciliationTask = Task.Run(async () =>
		{
			await previous;
			await CleanupWorkerTopicsAsync(token);
			await ReconcileTopicNamesAsync(token);
		});
		_log.Information("TelegramBridgeService started. Known topics: {Count}", _nodeIdByThreadId.Count);
	}

	public void Stop()
	{
		_daemon.EntryAppended -= OnEntryAppended;
		_cts?.Cancel();
		_cts = null;
	}

	public async Task EnsureTopicForSessionAsync(SessionNodeModel node)
	{
		if (!_appSettings.Settings.TelegramEnabled || _appSettings.Settings.TelegramChatId == 0)
			return;
		if (node.OrchestrationDepth > 0) // FR.21.19 — Worker Sessions never get a topic
			return;
		if (_appSettings.Settings.TelegramTopicThreadIdsByNodeId.ContainsKey(node.NodeId))
			return;

		await _topicLock.WaitAsync();
		try
		{
			// Re-check inside the lock — a concurrent caller may have just created it.
			if (_appSettings.Settings.TelegramTopicThreadIdsByNodeId.ContainsKey(node.NodeId))
				return;

			var topicName = BuildTopicName(node);
			var threadId = await _client.CreateForumTopicAsync(_appSettings.Settings.TelegramChatId, topicName);

			lock (_appSettings.SyncRoot)
			{
				_appSettings.Settings.TelegramTopicThreadIdsByNodeId[node.NodeId] = threadId;
				_appSettings.Settings.TelegramTopicNamesByNodeId[node.NodeId] = topicName;
				_appSettings.Save();
			}
			_nodeIdByThreadId[threadId] = node.NodeId;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "TelegramBridgeService: failed to create topic for session {NodeId}", node.NodeId);
		}
		finally
		{
			_topicLock.Release();
		}
	}

	/// <summary>The FR.21.2 topic name, shared by topic creation and reconciliation (untruncated; the client truncates).</summary>
	private string BuildTopicName(SessionNodeModel node) =>
		$"{_labelService.GetLabel(node.WorkingDirectory)} / {node.Name}";

	// ── Worker Session topic cleanup (FR.21.19) ─────────────────────────────────

	/// <summary>
	/// Deletes the Telegram topic of any session that turns out to be a Worker Session
	/// (<c>OrchestrationDepth &gt; 0</c>) and removes its mapping — covers topics created for worker
	/// sessions before this exclusion existed, and self-heals if one ever slips through again. An
	/// orphan (node no longer found) is left untouched, matching FR.21.18's existing orphan policy —
	/// there is no way to tell whether a since-deleted node was a worker.
	/// </summary>
	private async Task CleanupWorkerTopicsAsync(CancellationToken ct)
	{
		if (_appSettings.Settings.TelegramChatId == 0)
			return;

		try
		{
			KeyValuePair<string, int>[] snapshot;
			lock (_appSettings.SyncRoot)
				snapshot = _appSettings.Settings.TelegramTopicThreadIdsByNodeId.ToArray();

			foreach (var (nodeId, threadId) in snapshot)
			{
				ct.ThrowIfCancellationRequested();

				var node = _daemon.FindSessionByNodeId(nodeId);
				if (node == null || node.OrchestrationDepth == 0)
					continue;

				try
				{
					await _client.DeleteForumTopicAsync(_appSettings.Settings.TelegramChatId, threadId, ct);
				}
				catch (TelegramApiException ex)
				{
					_log.Warning("TelegramBridgeService: failed to delete worker-session topic {ThreadId} of session {NodeId} ({Code}: {Description}); will retry on next start",
						threadId, nodeId, ex.ErrorCode, ex.Description);
					continue; // leave the mapping intact for a retry
				}

				lock (_appSettings.SyncRoot)
				{
					_appSettings.Settings.TelegramTopicThreadIdsByNodeId.Remove(nodeId);
					_appSettings.Settings.TelegramTopicNamesByNodeId.Remove(nodeId);
					_appSettings.Save();
				}
				_nodeIdByThreadId.TryRemove(threadId, out _);

				await DelayAsync(TimeSpan.FromMilliseconds(TopicRenameNoOpIntervalMs), ct);
			}
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			// Bridge stopped — quiet exit.
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "TelegramBridgeService: worker-session topic cleanup stopped; it will retry on next start");
		}
	}

	// ── Topic name reconciliation (FR.21.18) ────────────────────────────────────

	private async Task ReconcileTopicNamesAsync(CancellationToken ct)
	{
		if (_appSettings.Settings.TelegramChatId == 0)
			return;

		var unsaved = 0;
		try
		{
			KeyValuePair<string, int>[] snapshot;
			lock (_appSettings.SyncRoot)
				snapshot = _appSettings.Settings.TelegramTopicThreadIdsByNodeId.ToArray();

			foreach (var (nodeId, threadId) in snapshot)
			{
				ct.ThrowIfCancellationRequested();

				var node = _daemon.FindSessionByNodeId(nodeId);
				if (node == null)
					continue; // orphan — leave the mapping untouched

				var desired = BuildTopicName(node);
				string? applied;
				lock (_appSettings.SyncRoot)
					_appSettings.Settings.TelegramTopicNamesByNodeId.TryGetValue(nodeId, out applied);
				if (applied == desired)
					continue;

				bool renamed;
				try
				{
					renamed = await EditTopicWithRetryAsync(threadId, desired, ct);
				}
				catch (TelegramApiException ex) when (IsChatLevelError(ex))
				{
					_log.Warning("TelegramBridgeService: topic rename stopped, chat-level error ({Code}: {Description})", ex.ErrorCode, ex.Description);
					return;
				}
				catch (TelegramApiException ex) when (ex.ErrorCode == 429)
				{
					_log.Warning("TelegramBridgeService: topic rename stopped, rate limit hit again after retry ({Description})", ex.Description);
					return;
				}
				catch (TelegramApiException ex)
				{
					_log.Warning("TelegramBridgeService: could not rename topic {ThreadId} of session {NodeId} ({Code}: {Description}); skipping", threadId, nodeId, ex.ErrorCode, ex.Description);
					await DelayAsync(TimeSpan.FromMilliseconds(TopicRenameNoOpIntervalMs), ct);
					continue;
				}

				lock (_appSettings.SyncRoot)
				{
					_appSettings.Settings.TelegramTopicNamesByNodeId[nodeId] = desired;
					if (++unsaved >= Constants.Telegram.TopicRenameSaveBatchSize)
					{
						_appSettings.Save();
						unsaved = 0;
					}
				}

				await DelayAsync(TimeSpan.FromMilliseconds(renamed ? TopicRenameIntervalMs : TopicRenameNoOpIntervalMs), ct);
			}
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			// Bridge stopped — quiet exit. (A cancellation without a stop request, e.g. an HttpClient timeout, is a network failure below.)
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "TelegramBridgeService: topic name reconciliation stopped; it will retry on next start");
		}
		finally
		{
			if (unsaved > 0)
			{
				try
				{
					lock (_appSettings.SyncRoot)
						_appSettings.Save();
				}
				catch (Exception ex)
				{
					_log.Warning(ex, "TelegramBridgeService: failed to save topic names");
				}
			}
		}
	}

	/// <summary>Edits the topic; on HTTP 429 waits <c>retry_after</c> and retries once. Returns true when renamed, false when not modified.</summary>
	private async Task<bool> EditTopicWithRetryAsync(int threadId, string name, CancellationToken ct)
	{
		var chatId = _appSettings.Settings.TelegramChatId;
		try
		{
			return await _client.EditForumTopicAsync(chatId, threadId, name, ct);
		}
		catch (TelegramApiException ex) when (ex.ErrorCode == 429)
		{
			await DelayAsync(TimeSpan.FromSeconds(Math.Clamp(ex.RetryAfterSeconds ?? 1, 1, 300)), ct);
			return await _client.EditForumTopicAsync(chatId, threadId, name, ct);
		}
	}

	/// <summary>Rights problems and a missing chat affect every topic — the run stops instead of skipping.</summary>
	private static bool IsChatLevelError(TelegramApiException ex) =>
		ex.ErrorCode is 401 or 403
		|| ex.Description.Contains("not enough rights", StringComparison.OrdinalIgnoreCase)
		|| ex.Description.Contains("chat not found", StringComparison.OrdinalIgnoreCase);

	public async Task<bool> NotifyAsync(string nodeId, string message)
	{
		if (!_appSettings.Settings.TelegramEnabled)
			return false;

		try
		{
			var node = _daemon.FindSessionByNodeId(nodeId);
			if (node == null)
				return false;
			if (node.OrchestrationDepth > 0) // FR.21.19 — Worker Sessions never post to Telegram
				return false;

			await EnsureTopicForSessionAsync(node);

			int? threadId = _appSettings.Settings.TelegramTopicThreadIdsByNodeId.TryGetValue(nodeId, out var t) ? t : null;

			// FR.21.10 — a notification is a ping directed at the user, not mirrored conversation
			// text (FR.21.9), so it carries the mention handle when one is configured.
			var handle = _appSettings.Settings.TelegramMentionHandle;
			var text = string.IsNullOrWhiteSpace(handle) ? message : $"{handle} {message}";

			await _client.SendMessageAsync(_appSettings.Settings.TelegramChatId, text, threadId);
			return true;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "TelegramBridgeService: NotifyAsync failed for node {NodeId}", nodeId);
			return false;
		}
	}

	// ── Universal I/O mirror (FR.21.9) ──────────────────────────────────────────

	private void OnEntryAppended(object? sender, SessionEntryAppendedEventArgs e)
	{
		if (!_appSettings.Settings.TelegramEnabled)
			return;
		if (e.Entry.Role == Constants.SessionFile.RoleCompaction || string.IsNullOrWhiteSpace(e.Entry.Content))
			return;

		var node = _daemon.FindSessionByFileName(e.FileName);
		if (node == null)
			return;
		if (node.OrchestrationDepth > 0) // FR.21.19 — Worker Sessions are never mirrored
			return;

		if (e.Entry.Role == Constants.SessionFile.RoleUser
			&& _suppressNextUserAppend.TryRemove(node.NodeId, out var suppressedText)
			&& suppressedText == e.Entry.Content)
			return; // Echo suppression — this is the Telegram-origin message the user just typed.

		_ = MirrorEntryAsync(node, e.Entry);
	}

	private async Task MirrorEntryAsync(SessionNodeModel node, SessionEntryModel entry)
	{
		try
		{
			await EnsureTopicForSessionAsync(node);
			if (!_appSettings.Settings.TelegramTopicThreadIdsByNodeId.TryGetValue(node.NodeId, out var threadId))
				return;

			var prefix = entry.Role switch
			{
				Constants.SessionFile.RoleUser => "USER: ",
				Constants.SessionFile.RoleSystem => "⚙️ ",
				_ => string.Empty, // ASSISTANT — unprefixed, reads as the bot itself is Claude
			};

			foreach (var chunk in ChunkMessage(prefix + entry.Content, Constants.Telegram.MaxMessageLength))
				await _client.SendMessageAsync(_appSettings.Settings.TelegramChatId, chunk, threadId);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "TelegramBridgeService: failed to mirror entry for node {NodeId}", node.NodeId);
		}
	}

	/// <summary>Splits text over <paramref name="maxLen"/> chars, preferring the last newline within each window.</summary>
	private static IEnumerable<string> ChunkMessage(string text, int maxLen)
	{
		if (text.Length <= maxLen)
		{
			yield return text;
			yield break;
		}

		var start = 0;
		while (start < text.Length)
		{
			var len = Math.Min(maxLen, text.Length - start);
			if (start + len < text.Length)
			{
				var lastNewline = text.LastIndexOf('\n', start + len - 1, len);
				if (lastNewline > start)
					len = lastNewline - start + 1;
			}

			yield return text.Substring(start, len).TrimEnd('\n');
			start += len;
		}
	}

	// ── Poll loop ─────────────────────────────────────────────────────────────

	private async Task PollLoopAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				var updates = await _client.GetUpdatesAsync(
					_appSettings.Settings.TelegramUpdateOffset, Constants.Telegram.LongPollTimeoutSeconds, ct);

				Acknowledge(updates);
				var actionable = updates.Where(u => u.IsActionable).ToList();

				// FR.21.16 — a batch ending in a media group may be split from the rest of its album:
				// one short extra poll (from the already-advanced offset) before dispatching it.
				if (actionable.Count > 0 && actionable[^1].MediaGroupId != null)
				{
					var followUp = await _client.GetUpdatesAsync(
						_appSettings.Settings.TelegramUpdateOffset, Constants.Telegram.AlbumFollowUpPollSeconds, ct);
					Acknowledge(followUp);
					actionable.AddRange(followUp.Where(u => u.IsActionable));
				}

				foreach (var update in CoalesceAlbums(actionable))
				{
					try
					{
						await HandleUpdateAsync(update);
					}
					catch (Exception ex)
					{
						_log.Warning(ex, "TelegramBridgeService: failed to handle update {UpdateId}", update.UpdateId);
					}
				}
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				return;
			}
			catch (Exception ex)
			{
				_log.Warning(ex, "TelegramBridgeService: polling loop failed; retrying in 5s");
				try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
				catch (OperationCanceledException) { return; }
			}
		}
	}

	/// <summary>FR.21.12: advances and persists the offset past EVERY update in the batch, actionable or not.</summary>
	private void Acknowledge(IReadOnlyList<TelegramUpdate> updates)
	{
		if (updates.Count == 0)
			return;

		var next = updates.Max(u => u.UpdateId) + 1;
		if (next > _appSettings.Settings.TelegramUpdateOffset)
			_appSettings.Settings.TelegramUpdateOffset = next;
		_appSettings.Save();
	}

	/// <summary>
	/// FR.21.16: merges messages sharing a <c>media_group_id</c> (same chat + thread) into one update at
	/// the position of the group's first message — first non-empty caption wins, attachments in order.
	/// Updates without a media group pass through unchanged.
	/// </summary>
	private static IEnumerable<TelegramUpdate> CoalesceAlbums(IReadOnlyList<TelegramUpdate> updates)
	{
		var groups = updates
			.Where(u => u.MediaGroupId != null)
			.GroupBy(u => (u.ChatId, u.MessageThreadId, u.MediaGroupId))
			.ToDictionary(g => g.Key, g => g.ToList());
		var emitted = new HashSet<(long, int?, string?)>();

		foreach (var update in updates)
		{
			if (update.MediaGroupId == null)
			{
				yield return update;
				continue;
			}

			var key = (update.ChatId, update.MessageThreadId, update.MediaGroupId);
			if (!emitted.Add(key))
				continue;

			var members = groups[key];
			yield return members[0] with
			{
				Text = members.Select(m => m.Text).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)) ?? string.Empty,
				Attachments = members.SelectMany(m => m.Attachments ?? Array.Empty<TelegramAttachment>()).ToList(),
			};
		}
	}

	private async Task HandleUpdateAsync(TelegramUpdate update)
	{
		if (update.ChatId != _appSettings.Settings.TelegramChatId)
		{
			_log.Warning("Ignoring Telegram message from unauthorized chat {ChatId}", update.ChatId);
			return;
		}

		if (!_appSettings.Settings.TelegramAllowedUserIds.Contains(update.FromUserId))
		{
			_log.Warning("Ignoring Telegram message from unauthorized user {UserId}", update.FromUserId);
			return;
		}

		var text = update.Text.Trim();

		// Slash commands are only recognized in plain text messages; a caption on an attachment
		// is prompt text (FR.21.13).
		var isPlainText = !update.HasAttachments;

		if (isPlainText && text.Equals("/sessions", StringComparison.OrdinalIgnoreCase))
		{
			await _client.SendMessageAsync(update.ChatId, BuildSessionsList(), update.MessageThreadId);
			return;
		}

		if (isPlainText && text.StartsWith("/open", StringComparison.OrdinalIgnoreCase))
		{
			var arg = text.Length > 5 ? text[5..].Trim() : string.Empty;
			await _client.SendMessageAsync(update.ChatId, BuildOpenReply(arg), update.MessageThreadId);
			return;
		}

		if (update.MessageThreadId is int threadId && _nodeIdByThreadId.TryGetValue(threadId, out var nodeId))
		{
			// FR.21.11 — /stop cancels the mapped session's in-flight turn rather than being routed
			// to it as a prompt.
			if (isPlainText && text.Equals("/stop", StringComparison.OrdinalIgnoreCase))
			{
				var cancelled = _daemon.CancelTurn(nodeId);
				await _client.SendMessageAsync(update.ChatId,
					cancelled ? "Turn cancelled." : "No turn is currently running.", threadId);
				return;
			}

			// Fire-and-forget: a turn can run for minutes, and must never stall the poll loop from
			// processing other updates (other sessions, other topics) or picking up the next batch.
			_ = RouteToSessionAsync(nodeId, text, update, update.ChatId, update.MessageThreadId);
			return;
		}

		await _client.SendMessageAsync(update.ChatId,
			"Unrecognized command or topic. Use /sessions to list active sessions, /open <name> to read one, or reply inside a session's own topic (/stop cancels its running turn).",
			update.MessageThreadId);
	}

	/// <summary>
	/// Runs a turn on the target session. Not awaited by the poll loop. The turn's own input/output
	/// reaches the topic via the Universal I/O Mirror (FR.21.9, <see cref="OnEntryAppended"/>) as
	/// <c>SessionTurnService.RunTurnAsync</c> appends it — there is no separate "post the reply" step.
	/// </summary>
	private Task RouteToSessionAsync(string nodeId, string messageText, TelegramUpdate update, long chatId, int? messageThreadId) =>
		Task.Run(async () =>
		{
			var node = _daemon.FindSessionByNodeId(nodeId);
			if (node == null)
			{
				await _client.SendMessageAsync(chatId, "This session no longer exists.", messageThreadId);
				return;
			}

			// FR.21.14/15 — attachments are downloaded here (off the poll loop) and appended as lines.
			// DownloadAsync turns every per-file failure into a line, so the text is never lost.
			var text = messageText;
			if (update.HasAttachments)
			{
				var lines = await _inbox.DownloadAsync(update.UpdateId, update.Attachments!);
				text = TelegramInbox.BuildTurnText(messageText, lines);
			}

			_suppressNextUserAppend[nodeId] = text;
			try
			{
				await _daemon.RunTurnAsync(node, text, TurnSource.Telegram);
			}
			catch (Exception ex)
			{
				_suppressNextUserAppend.TryRemove(nodeId, out _);
				_log.Warning(ex, "TelegramBridgeService: turn failed for node {NodeId}", nodeId);
				try { await _client.SendMessageAsync(chatId, $"⚠️ Turn failed: {ex.Message}", messageThreadId); }
				catch { /* best effort */ }
			}
		});

	private string BuildSessionsList()
	{
		var summaries = _daemon.BuildAgentSessionSummaries()
			.Where(s => s.OrchestrationDepth == 0) // FR.21.19 — Worker Sessions never appear, favorited or not
			.Where(s => s.IsFavorite || _appSettings.Settings.TelegramTopicThreadIdsByNodeId.ContainsKey(s.NodeId))
			.ToList();

		if (summaries.Count == 0)
			return "No favorited or active sessions yet.";

		var lines = summaries.Select(s =>
			$"{(s.IsRunning ? "🟢" : "⚪")} {s.DirectoryLabel} / {s.Name}" + (s.IsFavorite ? " ⭐" : ""));
		return string.Join("\n", lines);
	}

	private string BuildOpenReply(string arg)
	{
		if (string.IsNullOrWhiteSpace(arg))
			return "Usage: /open <session name or NodeId prefix>";

		var summaries = _daemon.BuildAgentSessionSummaries();
		var match = summaries.FirstOrDefault(s => s.NodeId.StartsWith(arg, StringComparison.OrdinalIgnoreCase))
			?? summaries.FirstOrDefault(s => s.Name.Contains(arg, StringComparison.OrdinalIgnoreCase));

		if (match == null)
			return $"No session matching '{arg}'.";

		var entries = _daemon.ReadEntries(_daemon.FindSessionByNodeId(match.NodeId)!.FileName)
			.Where(e => e.Role is Constants.SessionFile.RoleUser or Constants.SessionFile.RoleAssistant)
			.TakeLast(Constants.Telegram.OpenCommandEntryCount)
			.ToList();

		if (entries.Count == 0)
			return $"{match.DirectoryLabel} / {match.Name}: no conversation entries.";

		var sb = new StringBuilder();
		sb.AppendLine($"{match.DirectoryLabel} / {match.Name}");
		foreach (var e in entries)
			sb.AppendLine($"[{e.Role}] {Truncate(e.Content, 500)}");

		return Truncate(sb.ToString().TrimEnd(), 3500);
	}

	private static string Truncate(string text, int maxChars) =>
		text.Length > maxChars ? text[..maxChars].TrimEnd() + "…" : text;
}
