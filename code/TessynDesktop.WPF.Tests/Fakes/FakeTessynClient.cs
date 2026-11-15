using System.Text.Json;
using TessynDesktop.Client;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An in-memory <see cref="ITessynClient"/> with scriptable tree, workers, search and transcripts; records every call.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class FakeTessynClient : ITessynClient
{
	private readonly object _gate = new();

	public TessynClientState State { get; private set; } = TessynClientState.Connected;

	public HelloResult? ServerInfo { get; set; }

	public event EventHandler<TessynClientStateChangedEventArgs>? StateChanged;

	public event EventHandler<RemoteNotification>? NotificationReceived;

	public event EventHandler<ResyncedEventArgs>? Resynced;

	/// <summary>Every call as "method:argument", in order.</summary>
	public List<string> Calls { get; } = [];

	public long TreeVersion { get; set; } = 1;

	public List<TreeDirectoryDto> Directories { get; set; } = [];

	public Dictionary<string, List<TreeSessionDto>> Workers { get; } = [];

	public Func<string, TreeSearchResult>? SearchHandler { get; set; }

	public Dictionary<string, FakeSession> Sessions { get; } = [];

	public Exception? ConnectException { get; set; }

	/// <summary>When set, <see cref="SessionGetAsync"/> waits for it (to deliver events while a call is pending).</summary>
	public TaskCompletionSource? SessionGetGate { get; set; }

	/// <summary>When set, older-page requests (beforeIndex) wait for it.</summary>
	public TaskCompletionSource? OlderGate { get; set; }

	public bool Disposed { get; private set; }

	/// <summary>When set, <see cref="TreeSnapshotAsync"/> throws it.</summary>
	public Exception? TreeSnapshotException { get; set; }

	public int SendCount => Calls.Count(c => c.StartsWith("turn.send:", StringComparison.Ordinal));

	public void SetState(TessynClientState state)
	{
		var previous = State;
		State = state;
		StateChanged?.Invoke(this, new TessynClientStateChangedEventArgs(previous, state));
	}

	public void RaiseNotification(string method, object payload)
	{
		var node = JsonSerializer.SerializeToNode(payload, payload.GetType(), TdcpJsonOptions.Default)!;
		NotificationReceived?.Invoke(this, new RemoteNotification(method, node));
	}

	/// <summary>Delivers a notification from its wire JSON, as the daemon wrote it (golden frames).</summary>
	public void RaiseRaw(string method, string json) => NotificationReceived?.Invoke(this, new RemoteNotification(method, System.Text.Json.Nodes.JsonNode.Parse(json)!));

	public void RaiseResync(string reason = RemoteProtocol.ResyncReasons.Gap) => Resynced?.Invoke(this, new ResyncedEventArgs(reason, 0));

	public Task ConnectAsync(CancellationToken cancellationToken = default)
	{
		Calls.Add("connect");
		if (ConnectException != null)
			throw ConnectException;
		return Task.CompletedTask;
	}

	/// <summary>Every <c>turn.send</c> in order, with the model, effort, profile and request id it carried.</summary>
	public List<FakeTurnSend> TurnSends { get; } = [];

	/// <summary>The answer of the next <c>turn.send</c>; null = accepted and not queued, with a fresh turn id.</summary>
	public Func<FakeTurnSend, TurnSendResult>? TurnSendHandler { get; set; }

	/// <summary>When set, <c>turn.send</c> throws it.</summary>
	public Exception? TurnSendException { get; set; }

	/// <summary>When set, <c>turn.send</c> waits for it after recording the call (a round trip in flight).</summary>
	public TaskCompletionSource? TurnSendGate { get; set; }

	public async Task<TurnSendResult> TurnSendAsync(string nodeId, string text, string? modelId = null, string? effort = null, string? profileId = null, string? clientRequestId = null, CancellationToken cancellationToken = default)
	{
		var send = new FakeTurnSend(nodeId, text, modelId, effort, profileId, clientRequestId);
		TurnSends.Add(send);
		Record($"turn.send:{nodeId}:{text}");
		if (TurnSendException != null)
			throw TurnSendException;

		if (TurnSendGate != null)
			await TurnSendGate.Task;

		return TurnSendHandler?.Invoke(send) ?? new TurnSendResult("turn-" + TurnSends.Count, true, false);
	}

	public Exception? TurnStopException { get; set; }

	public Task<SessionStopResult> TurnStopAsync(string nodeId, CancellationToken cancellationToken = default)
	{
		Record($"turn.stop:{nodeId}");
		if (TurnStopException != null)
			throw TurnStopException;

		return Task.FromResult(new SessionStopResult(true));
	}

	public void RaiseTurnStarted(string nodeId, string turnId, string? clientRequestId = null) => RaiseNotification(RemoteProtocol.TurnStream.StartedEvent, new TurnStartedPayload(nodeId, turnId, clientRequestId));

	public void RaiseTurnStatus(string nodeId, string turnId, TurnState state, string? detail = null) => RaiseNotification(RemoteProtocol.TurnStream.StatusEvent, new TurnStatusPayload(nodeId, turnId, state, detail));

	public void RaiseTurnDelta(string nodeId, string turnId, long seq, string text, TurnDeltaKind kind = TurnDeltaKind.Text) => RaiseNotification(RemoteProtocol.TurnStream.DeltaEvent, new TurnDeltaPayload(nodeId, turnId, seq, kind, text));

	public void RaiseTurnTool(string nodeId, string turnId, string toolUseId, string name, TurnToolStatus status, string? summary = null, bool? isError = null) =>
		RaiseNotification(RemoteProtocol.TurnStream.ToolEvent, new TurnToolPayload(nodeId, turnId, toolUseId, name, status, summary, isError));

	public void RaiseTurnUsage(string nodeId, string turnId, int input, int output, double cost) => RaiseNotification(RemoteProtocol.TurnStream.UsageEvent, new TurnUsagePayload(nodeId, turnId, input, output, cost));

	public void RaiseTurnCompleted(string nodeId, string turnId, bool cancelled = false, string? error = null) => RaiseNotification(RemoteProtocol.Events.TurnCompleted, new TurnCompletedPayload(nodeId, cancelled, error, turnId));
	public Task<HelloResult> HelloAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<TreeSnapshotResult> TreeSnapshotAsync(long? sinceVersion = null, CancellationToken cancellationToken = default)
	{
		Record($"tree.snapshot:{sinceVersion?.ToString() ?? "full"}");
		if (TreeSnapshotException != null)
			throw TreeSnapshotException;
		if (sinceVersion == TreeVersion)
			return Task.FromResult(new TreeSnapshotResult(TreeVersion, true, null));
		return Task.FromResult(new TreeSnapshotResult(TreeVersion, null, Directories));
	}

	public Task<TreeWorkersResult> TreeWorkersAsync(string supervisorId, CancellationToken cancellationToken = default)
	{
		Record($"tree.workers:{supervisorId}");
		Workers.TryGetValue(supervisorId, out var list);
		return Task.FromResult(new TreeWorkersResult(supervisorId, list ?? []));
	}

	public Task<TreeSearchResult> TreeSearchAsync(string query, bool? exact = null, string? scopeNodeId = null, CancellationToken cancellationToken = default)
	{
		Record($"tree.search:{query}");
		cancellationToken.ThrowIfCancellationRequested();
		return Task.FromResult(SearchHandler?.Invoke(query) ?? new TreeSearchResult(query, false, false, false, []));
	}

	public Task<CreateSessionResult> TreeCreateSessionAsync(string anchorId, string name, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<RecentListResult> RecentListAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<RecentHideResult> RecentHideAsync(string nodeId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<TagSearchResult> TagsSearchAsync(string text, IReadOnlyList<TagRefDto> selected, string? scopeNodeId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public async Task<SessionDetailDto> SessionGetAsync(string nodeId, CancellationToken cancellationToken = default)
	{
		Record($"session.get:{nodeId}");
		if (SessionGetGate != null)
			await SessionGetGate.Task;
		var session = Sessions[nodeId];
		return new SessionDetailDto(
			nodeId, session.Name, "dir", null, null!, null, session.State(nodeId), session.Tags,
			new AttentionSummaryDto(), session.LastUsed, false, session.Entries.Count, session.Epoch);
	}

	public async Task<SessionEntriesResult> SessionEntriesAsync(string nodeId, int? beforeIndex = null, int? afterIndex = null, int? limit = null, CancellationToken cancellationToken = default)
	{
		Record($"session.entries:{nodeId}:before={beforeIndex?.ToString() ?? "-"}:after={afterIndex?.ToString() ?? "-"}:limit={limit?.ToString() ?? "-"}");
		if (beforeIndex.HasValue && OlderGate != null)
			await OlderGate.Task;
		if (afterIndex.HasValue && AfterIndexException != null)
			throw AfterIndexException;
		var session = Sessions[nodeId];
		var total = session.Entries.Count;
		var max = limit ?? 100;
		int start;
		int end;
		if (afterIndex.HasValue)
		{
			start = Math.Clamp(afterIndex.Value + 1, 0, total);
			end = Math.Min(total, start + max);
		}
		else if (beforeIndex.HasValue)
		{
			end = Math.Clamp(beforeIndex.Value, 0, total);
			start = Math.Max(0, end - max);
		}
		else
		{
			end = total;
			start = Math.Max(0, end - max);
		}

		var result = new SessionEntriesResult(nodeId, session.Epoch, total, session.Entries.Skip(start).Take(end - start).ToList());
		if (afterIndex.HasValue && AfterPageHook != null)
		{
			var hook = AfterPageHook;
			AfterPageHook = null;   // once
			hook();                 // something happens after the page was computed and before the caller applies it
		}

		return result;
	}

	/// <summary>When set, every after-index entries call throws it (a daemon that keeps failing).</summary>
	public Exception? AfterIndexException { get; set; }

	/// <summary>Runs once, right after the next after-index page was computed (an append arriving "during" that page).</summary>
	public Action? AfterPageHook { get; set; }

	public Task<SessionEntryDto> SessionEntryAsync(string nodeId, int index, int epoch, CancellationToken cancellationToken = default)
	{
		Record($"session.entry:{nodeId}:{index}");
		var session = Sessions[nodeId];
		return Task.FromResult(session.FullEntries.TryGetValue(index, out var full) ? full : session.Entries[index]);
	}

	public Task<SessionSendResult> SessionSendAsync(string nodeId, string text, string? modelId = null, string? effort = null, string? profileId = null, CancellationToken cancellationToken = default)
	{
		Record($"session.send:{nodeId}:{text}");
		return Task.FromResult(new SessionSendResult(true, false));
	}

	public Task<SessionStopResult> SessionStopAsync(string nodeId, CancellationToken cancellationToken = default)
	{
		Record($"session.stop:{nodeId}");
		return Task.FromResult(new SessionStopResult(true));
	}

	public Task<SessionCompactResult> SessionCompactAsync(string nodeId, string? modelId = null, string? effort = null, string? profileId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<SetFavoriteResult> SessionSetFavoriteAsync(string nodeId, bool value, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<SetStreamingResult> SessionSetStreamingAsync(string nodeId, bool enabled, bool? force = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	/// <summary>The Focus items per session, as the daemon's ledger would list them.</summary>
	public Dictionary<string, List<AttentionItemDto>> Attention { get; } = [];

	/// <summary>When set, <see cref="AttentionListAsync"/> throws it.</summary>
	public Exception? AttentionListException { get; set; }

	public Task<AttentionListResult> AttentionListAsync(string nodeId, CancellationToken cancellationToken = default)
	{
		Record($"attention.list:{nodeId}");
		if (AttentionListException != null)
			throw AttentionListException;
		Attention.TryGetValue(nodeId, out var items);
		return Task.FromResult(new AttentionListResult(nodeId, new AttentionSummaryDto(), [.. items ?? []]));
	}

	public Task<AttentionReplyResult> AttentionReplyAsync(string nodeId, IReadOnlyList<AttentionAnswerParams> answers, string? modelId = null, string? effort = null, string? profileId = null, CancellationToken cancellationToken = default)
	{
		Record($"attention.reply:{nodeId}:{string.Join("|", answers.Select(a => a.ItemId + "=" + a.Text))}");
		var answered = new List<string>();
		var skipped = new List<string>();
		var items = Attention.GetValueOrDefault(nodeId) ?? [];
		foreach (var answer in answers)
		{
			var index = items.FindIndex(i => i.Id == answer.ItemId && i.Status == AttentionStatus.Open);
			if (index < 0 || string.IsNullOrWhiteSpace(answer.Text))
			{
				skipped.Add(answer.ItemId);
				continue;
			}

			items[index] = items[index] with { Status = AttentionStatus.Answered, Answer = answer.Text };
			answered.Add(answer.ItemId);
		}

		return Task.FromResult(new AttentionReplyResult(answered, skipped, answered.Count > 0));
	}

	public Task<AttentionDismissResult> AttentionDismissAsync(string nodeId, string itemId, CancellationToken cancellationToken = default)
	{
		Record($"attention.dismiss:{nodeId}:{itemId}");
		var items = Attention.GetValueOrDefault(nodeId) ?? [];
		var index = items.FindIndex(i => i.Id == itemId && i.Status == AttentionStatus.Open);
		if (index >= 0)
			items[index] = items[index] with { Status = AttentionStatus.Dismissed };
		return Task.FromResult(new AttentionDismissResult(index >= 0));
	}

	/// <summary>The answer of <see cref="CatalogGetAsync"/>.</summary>
	public CatalogResult Catalog { get; set; } = new([], [], []);

	/// <summary>When set, <see cref="CatalogGetAsync"/> throws it.</summary>
	public Exception? CatalogException { get; set; }

	public Task<CatalogResult> CatalogGetAsync(CancellationToken cancellationToken = default)
	{
		Record("catalog.get");
		if (CatalogException != null)
			throw CatalogException;
		return Task.FromResult(Catalog);
	}

	public Task<UsageResult> UsageGetAsync(string? profileId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	// P1.F / P1.G additions to ITessynClient; the WPF client does not use them yet.
	/// <summary>When set, around-entry queries wait for it (a jump still loading while the user does something else).</summary>
	public TaskCompletionSource? AroundGate { get; set; }

	/// <summary>When set, around-entry queries throw it.</summary>
	public Exception? AroundException { get; set; }

	public async Task<SessionEntriesResult> SessionEntriesAsync(string nodeId, SessionEntriesQuery query, CancellationToken cancellationToken = default)
	{
		Record($"session.entries.query:{nodeId}:around={query.AroundEntryId ?? "-"}");
		if (AroundGate != null)
			await AroundGate.Task;
		if (AroundException != null)
			throw AroundException;
		var session = Sessions[nodeId];
		var total = session.Entries.Count;
		var max = query.Limit ?? 100;
		if (query.AroundEntryId == null)
			throw new NotImplementedException("The fake only serves AroundEntryId.");

		var at = session.Entries.FindIndex(e => e.EntryId == query.AroundEntryId);
		if (at < 0)
			return new SessionEntriesResult(nodeId, session.Epoch, total, []);

		var start = Math.Clamp(at - max / 2, 0, Math.Max(0, total - max));
		return new SessionEntriesResult(nodeId, session.Epoch, total, session.Entries.Skip(start).Take(max).ToList());
	}

	public Task<SessionEntryDto> SessionEntryByIdAsync(string nodeId, string entryId, EntryRangeParams? range = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<SessionHeadersResult> SessionHeadersAsync(IReadOnlyList<string> nodeIds, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	/// <summary>Files by handle for <see cref="FileReadAsync"/>: name, MIME type and bytes.</summary>
	public Dictionary<string, (string Name, string Mime, byte[] Bytes)> Files { get; } = [];

	/// <summary>Handles <see cref="FilePreviewAsync"/> issues, by "nodeId|entryId|path".</summary>
	public Dictionary<string, string> PreviewHandles { get; } = [];

	/// <summary>When set, every <see cref="FileReadAsync"/> call after the first waits for it (to cancel a load in flight).</summary>
	public TaskCompletionSource? FileReadGate { get; set; }

	/// <summary>When set, answers every <see cref="FileReadAsync"/> itself (a daemon that lies about sizes or never ends a file).</summary>
	public Func<string, long, int?, FileReadResult>? FileReadHandler { get; set; }

	public async Task<FileReadResult> FileReadAsync(string handle, long? offset = null, int? length = null, CancellationToken cancellationToken = default)
	{
		Record($"file.read:{handle}:{offset ?? 0}:{length?.ToString() ?? "-"}");
		cancellationToken.ThrowIfCancellationRequested();
		if (FileReadHandler != null)
			return FileReadHandler(handle, offset ?? 0, length);
		if (!Files.TryGetValue(handle, out var file))
			throw new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "Unknown file handle");

		var start = (int)(offset ?? 0);
		if (start > 0 && FileReadGate != null)
			await FileReadGate.Task.WaitAsync(cancellationToken);

		var count = Math.Min(length ?? file.Bytes.Length, file.Bytes.Length - start);
		var slice = file.Bytes.AsSpan(start, count).ToArray();
		var isText = file.Mime.StartsWith("text/", StringComparison.Ordinal);
		return new FileReadResult(file.Name, file.Mime, file.Bytes.Length, DateTimeOffset.UnixEpoch, start, count, isText ? "utf8" : "base64",
			isText ? System.Text.Encoding.UTF8.GetString(slice) : Convert.ToBase64String(slice), start + count >= file.Bytes.Length);
	}

	public Task<FilePreviewResult> FilePreviewAsync(string nodeId, string entryId, string path, CancellationToken cancellationToken = default)
	{
		Record($"file.preview:{nodeId}:{entryId}:{path}");
		if (!PreviewHandles.TryGetValue($"{nodeId}|{entryId}|{path}", out var handle))
			throw new TessynRpcException(RemoteProtocol.ErrorCodes.InvalidParams, "The path does not occur in this entry");

		var file = Files[handle];
		return Task.FromResult(new FilePreviewResult(handle, file.Name, file.Mime, file.Bytes.Length, DateTimeOffset.UnixEpoch));
	}

	/// <summary>When set, answers every <see cref="AttachmentImportAsync"/> (nodeId, sourcePath); throw a <see cref="TessynRpcException"/> to refuse. Default: succeeds with a path under <c>a temp folder</c>.</summary>
	public Func<string, string, CancellationToken, Task<AttachmentDto>>? AttachmentImportHandler { get; set; }

	public Task<AttachmentDto> AttachmentImportAsync(string nodeId, string sourcePath, CancellationToken cancellationToken = default)
	{
		Record($"attachment.import:{nodeId}:{sourcePath}");
		if (AttachmentImportHandler != null)
			return AttachmentImportHandler(nodeId, sourcePath, cancellationToken);

		var name = Path.GetFileName(sourcePath);
		return Task.FromResult(new AttachmentDto("att." + name, Path.Combine(Path.GetTempPath(), "attachments", nodeId, "20260101-000000-" + name), name, 1, "application/octet-stream", DateTimeOffset.UnixEpoch));
	}

	public Task<AttachmentListResult> AttachmentListAsync(string nodeId, CancellationToken cancellationToken = default)
	{
		Record($"attachment.list:{nodeId}");
		return Task.FromResult(new AttachmentListResult(nodeId, []));
	}

	public ValueTask DisposeAsync()
	{
		Disposed = true;
		return ValueTask.CompletedTask;
	}

	private void Record(string call)
	{
		lock (_gate)
			Calls.Add(call);
	}
}
