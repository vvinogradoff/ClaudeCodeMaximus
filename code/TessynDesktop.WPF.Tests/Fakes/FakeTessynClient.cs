using System.Text.Json;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>An in-memory <see cref="ITessynClient"/> with scriptable tree, workers, search and transcripts; records every call.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeTessynClient : ITessynClient
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

	public int SendCount => Calls.Count(c => c.StartsWith("session.send:", StringComparison.Ordinal));

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

	public void RaiseResync(string reason = RemoteProtocol.ResyncReasons.Gap) => Resynced?.Invoke(this, new ResyncedEventArgs(reason, 0));

	public Task ConnectAsync(CancellationToken cancellationToken = default)
	{
		Calls.Add("connect");
		if (ConnectException != null)
			throw ConnectException;
		return Task.CompletedTask;
	}

	public Task<HelloResult> HelloAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<TreeSnapshotResult> TreeSnapshotAsync(long? sinceVersion = null, CancellationToken cancellationToken = default)
	{
		Record($"tree.snapshot:{sinceVersion?.ToString() ?? "full"}");
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
			new AttentionSummaryDto(), null!, false, session.Entries.Count, session.Epoch);
	}

	public async Task<SessionEntriesResult> SessionEntriesAsync(string nodeId, int? beforeIndex = null, int? afterIndex = null, int? limit = null, CancellationToken cancellationToken = default)
	{
		Record($"session.entries:{nodeId}:before={beforeIndex?.ToString() ?? "-"}:after={afterIndex?.ToString() ?? "-"}:limit={limit?.ToString() ?? "-"}");
		if (beforeIndex.HasValue && OlderGate != null)
			await OlderGate.Task;
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

		return new SessionEntriesResult(nodeId, session.Epoch, total, session.Entries.Skip(start).Take(end - start).ToList());
	}

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

	public Task<AttentionListResult> AttentionListAsync(string nodeId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<AttentionReplyResult> AttentionReplyAsync(string nodeId, IReadOnlyList<AttentionAnswerParams> answers, string? modelId = null, string? effort = null, string? profileId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<AttentionDismissResult> AttentionDismissAsync(string nodeId, string itemId, CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<CatalogResult> CatalogGetAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

	public Task<UsageResult> UsageGetAsync(string? profileId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

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
