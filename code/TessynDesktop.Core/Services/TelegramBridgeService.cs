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
	private static readonly ILogger _log = Log.ForContext<TelegramBridgeService>();

	private readonly IAppSettingsService _appSettings;
	private readonly ITelegramClient _client;
	private readonly IDaemonClient _daemon;
	private readonly IDirectoryLabelService _labelService;

	// In-memory reverse lookup (thread → NodeId) rebuilt from the persisted forward map on Start().
	// The forward map (NodeId → thread) lives in AppSettingsModel and is the source of truth.
	private readonly ConcurrentDictionary<int, string> _nodeIdByThreadId = new();
	private readonly SemaphoreSlim _topicLock = new(1, 1);

	// FR.21.9 echo suppression: the ROLE=USER entry SessionTurnService.RunTurnAsync appends for a
	// TurnSource.Telegram turn is the same text the user just typed into the topic — set immediately
	// before routing a turn, consumed (and removed) by the first matching append the mirror sees.
	private readonly ConcurrentDictionary<string, string> _suppressNextUserAppend = new();

	private CancellationTokenSource? _cts;

	public TelegramBridgeService(
		IAppSettingsService appSettings,
		ITelegramClient client,
		IDaemonClient daemon,
		IDirectoryLabelService labelService)
	{
		_appSettings = appSettings;
		_client = client;
		_daemon = daemon;
		_labelService = labelService;
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

		_daemon.EntryAppended += OnEntryAppended;

		_cts = new CancellationTokenSource();
		_ = PollLoopAsync(_cts.Token);
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
		if (_appSettings.Settings.TelegramTopicThreadIdsByNodeId.ContainsKey(node.NodeId))
			return;

		await _topicLock.WaitAsync();
		try
		{
			// Re-check inside the lock — a concurrent caller may have just created it.
			if (_appSettings.Settings.TelegramTopicThreadIdsByNodeId.ContainsKey(node.NodeId))
				return;

			var label = _labelService.GetLabel(node.WorkingDirectory);
			var topicName = $"{label} / {node.Name}";
			var threadId = await _client.CreateForumTopicAsync(_appSettings.Settings.TelegramChatId, topicName);

			_appSettings.Settings.TelegramTopicThreadIdsByNodeId[node.NodeId] = threadId;
			_nodeIdByThreadId[threadId] = node.NodeId;
			_appSettings.Save();
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

	public async Task<bool> NotifyAsync(string nodeId, string message)
	{
		if (!_appSettings.Settings.TelegramEnabled)
			return false;

		try
		{
			var node = _daemon.FindSessionByNodeId(nodeId);
			if (node == null)
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

				foreach (var update in updates)
				{
					_appSettings.Settings.TelegramUpdateOffset = update.UpdateId + 1;

					try
					{
						await HandleUpdateAsync(update);
					}
					catch (Exception ex)
					{
						_log.Warning(ex, "TelegramBridgeService: failed to handle update {UpdateId}", update.UpdateId);
					}
				}

				if (updates.Count > 0)
					_appSettings.Save();
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

		if (text.Equals("/sessions", StringComparison.OrdinalIgnoreCase))
		{
			await _client.SendMessageAsync(update.ChatId, BuildSessionsList(), update.MessageThreadId);
			return;
		}

		if (text.StartsWith("/open", StringComparison.OrdinalIgnoreCase))
		{
			var arg = text.Length > 5 ? text[5..].Trim() : string.Empty;
			await _client.SendMessageAsync(update.ChatId, BuildOpenReply(arg), update.MessageThreadId);
			return;
		}

		if (update.MessageThreadId is int threadId && _nodeIdByThreadId.TryGetValue(threadId, out var nodeId))
		{
			// FR.21.11 — /stop cancels the mapped session's in-flight turn rather than being routed
			// to it as a prompt.
			if (text.Equals("/stop", StringComparison.OrdinalIgnoreCase))
			{
				var cancelled = _daemon.CancelTurn(nodeId);
				await _client.SendMessageAsync(update.ChatId,
					cancelled ? "Turn cancelled." : "No turn is currently running.", threadId);
				return;
			}

			// Fire-and-forget: a turn can run for minutes, and must never stall the poll loop from
			// processing other updates (other sessions, other topics) or picking up the next batch.
			_ = RouteToSessionAsync(nodeId, text, update.ChatId, update.MessageThreadId);
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
	private Task RouteToSessionAsync(string nodeId, string text, long chatId, int? messageThreadId) =>
		Task.Run(async () =>
		{
			var node = _daemon.FindSessionByNodeId(nodeId);
			if (node == null)
			{
				await _client.SendMessageAsync(chatId, "This session no longer exists.", messageThreadId);
				return;
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
