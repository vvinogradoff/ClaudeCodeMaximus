using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IRemoteNotificationPublisher" />
/// <remarks>
/// Events can fire inside the settings lock or on the CLI reader thread, so a handler only builds a key and registers a
/// coalesced publication; resolving a node from a file name and reading its state happen when the window ends, on a timer
/// thread. <see cref="ISessionEpochTracker"/> is a constructor dependency so that its own rewrite subscription is registered
/// before this class's, and an epoch is bumped before <c>session.reset</c> reads it. Created by Claude.
/// </remarks>
public sealed class RemoteNotificationPublisher : IRemoteNotificationPublisher
{
	private static readonly ILogger _log = Log.ForContext<RemoteNotificationPublisher>();

	private const string TreeKey = "tree";
	private const string IndexKey = "index";
	private const string ScheduleKey = "schedules";
	private const string NodeKeyPrefix = "node:";
	private const string FileKeyPrefix = "file:";
	private const string AllNodesKey = "*";

	private readonly IDaemonClient _daemon;
	private readonly ISessionFileService _files;
	private readonly IRemoteEventBus _bus;
	private readonly ITreeVersionTracker _treeVersion;
	private readonly ISessionEpochTracker _epochs;
	private readonly ISessionStateBuilder _states;
	private readonly ISessionAuthErrorTracker? _authErrors;
	private readonly IDraftService? _drafts;
	private readonly ISchedulerService? _scheduler;
	private readonly ISessionResumabilityCache? _resumable;
	private readonly object _scheduleGate = new();
	private Dictionary<string, string> _scheduleOwners = new(StringComparer.Ordinal);
	private readonly DraftRevisions _revisions = new();

	private readonly object _gate = new();
	private bool _started;

	// P1.F: entries.appended bookkeeping (grouped to keep merges easy).
	private readonly ISessionContentStore? _store;
	private readonly object _entriesGate = new();
	private readonly Dictionary<string, int> _pendingAppends = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, (Guid FileId, long Seq)> _publishedSeq = new(StringComparer.OrdinalIgnoreCase);

	/// <param name="store">
	/// The content store (P1.F). It is a dependency so that its own rewrite subscription is registered before this class's, and
	/// so that <c>entries.appended</c> and the file id of <c>session.reset</c> can be built. Without one neither is published.
	/// </param>
	public RemoteNotificationPublisher(
		IDaemonClient daemon,
		ISessionFileService files,
		IRemoteEventBus bus,
		ITreeVersionTracker treeVersion,
		ISessionEpochTracker epochs,
		ISessionStateBuilder states,
		ISessionContentStore? store = null,
		ISessionAuthErrorTracker? authErrors = null,
		IDraftService? drafts = null,
		ISchedulerService? scheduler = null,
		ISessionResumabilityCache? resumable = null)
	{
		_resumable   = resumable;
		_scheduler   = scheduler;
		_authErrors  = authErrors;
		_drafts      = drafts;
		_daemon      = daemon;
		_files       = files;
		_bus         = bus;
		_treeVersion = treeVersion;
		_epochs      = epochs;
		_states      = states;
		_store       = store;
	}

	/// <inheritdoc />
	public void Start()
	{
		try
		{
			lock (_gate)
			{
				if (_started)
					return;

				_started = true;
				_daemon.TreeChanged            += OnTreeChanged;
				_daemon.EntryAppended          += OnEntryAppended;
				_daemon.TurnCompleted          += OnTurnCompleted;
				_daemon.AttentionChanged       += OnAttentionChanged;
				_daemon.SessionTagsChanged     += OnSessionTagsChanged;
				_daemon.SessionIndexed         += OnSessionIndexed;
				_daemon.SessionSettingsChanged += OnSessionSettingsChanged;
				_files.SessionFileRewritten    += OnSessionFileRewritten;
				if (_authErrors != null) _authErrors.Changed += OnAuthErrorChanged;
				if (_resumable != null) _resumable.Changed += OnResumabilityChanged;
				if (_drafts != null) _drafts.DraftChanged += OnDraftChanged;
				if (_scheduler != null)
				{
					lock (_scheduleGate)
						_scheduleOwners = ScheduleOwners();
					_daemon.ScheduleChanged += OnScheduleChanged;
				}
			}
		}
		catch (Exception ex)
		{
			_log.Error(ex, "RemoteNotificationPublisher: could not start");
		}
	}

	/// <inheritdoc />
	public void Stop()
	{
		lock (_gate)
		{
			if (!_started)
				return;

			_started = false;
			_daemon.TreeChanged            -= OnTreeChanged;
			_daemon.EntryAppended          -= OnEntryAppended;
			_daemon.TurnCompleted          -= OnTurnCompleted;
			_daemon.AttentionChanged       -= OnAttentionChanged;
			_daemon.SessionTagsChanged     -= OnSessionTagsChanged;
			_daemon.SessionIndexed         -= OnSessionIndexed;
			_daemon.SessionSettingsChanged -= OnSessionSettingsChanged;
			_files.SessionFileRewritten    -= OnSessionFileRewritten;
			if (_authErrors != null) _authErrors.Changed -= OnAuthErrorChanged;
			if (_resumable != null) _resumable.Changed -= OnResumabilityChanged;
			if (_drafts != null) _drafts.DraftChanged -= OnDraftChanged;
			if (_scheduler != null) _daemon.ScheduleChanged -= OnScheduleChanged;
		}
	}

	private void OnTreeChanged(object? sender, TreeChangedEventArgs e) => Guard(() =>
	{
		// The marker for hosts that project the tree: the structural change was published with its SettingsModified.
		if (e.Kind == TreeChangeKind.DaemonMutation)
			return;

		if (e.Kind == TreeChangeKind.SessionIsRunningChanged)
		{
			if (e.Session != null)
			{
				PublishSessionChangedByNode(e.Session.NodeId);

				// A supervisor's state carries the count of its running workers (direct and nested): every supervisor above the session is refreshed too. The lookups run off this
				// thread (the event can fire inside a lock); the per-node coalescing window then merges a burst of workers starting or stopping into one session.changed per supervisor.
				var worker = e.Session;
				if (!string.IsNullOrEmpty(worker.SupervisorNodeId))
					ThreadPool.QueueUserWorkItem(_ => Guard(() => PublishSessionChangedForSupervisors(worker)));
			}
			return;
		}

		_treeVersion.Increment();
		_bus.PublishCoalesced(
			Constants.Remote.Events.TreeChanged, TreeKey,
			() => new TreeChangedPayload(_treeVersion.Current),
			Constants.Remote.Timings.TreeChangedWindow);
	});

	private void OnEntryAppended(object? sender, SessionEntryAppendedEventArgs e) => Guard(() =>
	{
		var fileName = e.FileName;

		_bus.PublishCoalesced(
			Constants.Remote.Events.SessionAppended, FileKeyPrefix + fileName,
			() => BuildEpochPayload(fileName),
			Constants.Remote.Timings.SessionAppendedWindow);

		if (_store != null)
		{
			SeedPublishedSeq(fileName);
			lock (_entriesGate)
				_pendingAppends[fileName] = _pendingAppends.GetValueOrDefault(fileName) + 1;

			_bus.PublishCoalesced(
				Constants.Remote.Events.EntriesAppended, FileKeyPrefix + fileName,
				() => BuildEntriesAppendedPayload(fileName),
				Constants.Remote.Timings.SessionAppendedWindow);
		}

		_bus.PublishCoalesced(
			Constants.Remote.Events.SessionChanged, FileKeyPrefix + fileName,
			() => BuildSessionChangedPayload(_daemon.FindSessionByFileName(fileName)),
			Constants.Remote.Timings.SessionChangedWindow);
	});

	private void OnTurnCompleted(object? sender, SessionTurnCompletedEventArgs e) => Guard(() =>
	{
		var fileName  = e.FileName;
		var cancelled = e.Cancelled;
		var error     = e.ErrorText;
		var turnId    = e.TurnId;

		// Every completion is its own notification (a unique key), but the work is deferred off this thread. The factory first
		// publishes the session's pending appended/changed notifications, so turn.completed never overtakes the last of them.
		_bus.PublishCoalesced(
			Constants.Remote.Events.TurnCompleted, Guid.NewGuid().ToString("N"),
			() =>
			{
				_bus.FlushPending(Constants.Remote.Events.SessionAppended, FileKeyPrefix + fileName);
				_bus.FlushPending(Constants.Remote.Events.EntriesAppended, FileKeyPrefix + fileName);
				_bus.FlushPending(Constants.Remote.Events.SessionChanged, FileKeyPrefix + fileName);

				var node = _daemon.FindSessionByFileName(fileName);
				if (node == null)
					return null;

				_bus.FlushPending(Constants.Remote.Events.SessionChanged, NodeKeyPrefix + node.NodeId);
				return new TurnCompletedPayload(node.NodeId, cancelled, error, turnId);
			},
			Constants.Remote.Timings.Immediate);
	});

	private void OnAttentionChanged(object? sender, AttentionChangedEventArgs e) => Guard(() =>
	{
		var nodeId = e.NodeId;

		_bus.PublishCoalesced(
			Constants.Remote.Events.AttentionChanged, NodeKeyPrefix + nodeId,
			() => new AttentionChangedPayload(nodeId, _daemon.GetAttentionSummary(nodeId).ToDto()),
			Constants.Remote.Timings.AttentionChangedWindow);

		PublishSessionChangedByNode(nodeId);
	});

	private void OnSessionTagsChanged(object? sender, SessionTagsChangedEventArgs e) => Guard(() =>
	{
		var nodeId = e.NodeId;

		_bus.PublishCoalesced(
			Constants.Remote.Events.TagsChanged, NodeKeyPrefix + (nodeId ?? AllNodesKey),
			() => new TagsChangedPayload(nodeId),
			Constants.Remote.Timings.TagsChangedWindow);
	});

	private void OnSessionIndexed(object? sender, SessionIndexedEventArgs e) => Guard(() =>
		_bus.PublishCoalesced(
			Constants.Remote.Events.IndexChanged, IndexKey,
			() => new IndexChangedPayload(),
			Constants.Remote.Timings.IndexChangedWindow));

	private void OnSessionSettingsChanged(object? sender, SessionSettingsChangedEventArgs e) =>
		Guard(() => PublishSessionChangedByNode(e.NodeId));

	// The auth error is part of the session state, so a change publishes session.changed like a running-flag change does.
	private void OnAuthErrorChanged(string nodeId) => Guard(() => PublishSessionChangedByNode(nodeId));

	// The resumable flag is part of the session state too: a sweep that flipped some sessions publishes session.changed for each of them.
	private void OnResumabilityChanged(IReadOnlyList<string> nodeIds) => Guard(() => { foreach (var nodeId in nodeIds) PublishSessionChangedByNode(nodeId); });

	// draft.changed is local-stream only (the relay filters it). The revision is read when the window ends, so a burst of keystrokes saves announces the last text only.
	private void OnDraftChanged(object? sender, DraftChangedEventArgs e) => Guard(() =>
	{
		var fileName = e.FileName;
		var itemId   = e.ItemId;
		_bus.PublishCoalesced(
			RemoteProtocol.Composer.DraftChangedEvent, FileKeyPrefix + fileName + (itemId == null ? string.Empty : Constants.ItemDraftInfix + itemId),
			() => BuildDraftChangedPayload(fileName, itemId),
			Constants.Composer.DraftChangedWindow);

		// HasDraft is part of the session state (and moves the last-activity time), so the row state is refreshed too. A Focus reply draft
		// (FR.23.19) moves only the last-activity time (FR.9.20), but it takes the same path. The activity service caches the drafts by
		// their file stamps, so the state read when the window ends already sees the write.
		_bus.PublishCoalesced(
			Constants.Remote.Events.SessionChanged, FileKeyPrefix + fileName,
			() => BuildSessionChangedPayload(_daemon.FindSessionByFileName(fileName)),
			Constants.Remote.Timings.SessionChangedWindow);
	});

	private DraftChangedPayload? BuildDraftChangedPayload(string fileName, string? itemId)
	{
		var node = _daemon.FindSessionByFileName(fileName);
		if (node == null || _drafts == null)
			return null;

		var revision = itemId == null ? _revisions.Of(_drafts.LoadDraft(fileName)) : _revisions.Of(_drafts.LoadItemDraftFull(fileName, itemId));
		return new DraftChangedPayload(node.NodeId, revision, itemId);
	}

	// schedule.changed is local-stream only (the relay filters it). The scheduler event does not say what changed, so when the window ends the schedule ids
	// are compared with the last ones seen: nothing added or removed (a schedule only fired) publishes nothing; otherwise the payload names the session when
	// exactly one is touched. A session whose HasSchedules moved gets its session.changed, published from the thread pool and not from this builder.
	private void OnScheduleChanged(object? sender, EventArgs e) => Guard(() =>
		_bus.PublishCoalesced(
			RemoteProtocol.Schedules.ChangedEvent, ScheduleKey,
			BuildScheduleChangedPayload,
			Constants.ScheduleApi.ChangedWindow));

	private ScheduleChangedPayload? BuildScheduleChangedPayload()
	{
		var now = ScheduleOwners();
		Dictionary<string, string> before;
		lock (_scheduleGate)
		{
			before = _scheduleOwners;
			_scheduleOwners = now;
		}

		var touched = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (id, node) in before)
		{
			if (!now.ContainsKey(id))
				touched.Add(node);
		}

		foreach (var (id, node) in now)
		{
			if (!before.ContainsKey(id))
				touched.Add(node);
		}

		if (touched.Count == 0)
			return null;

		var moved = touched.Where(node => before.ContainsValue(node) != now.ContainsValue(node)).ToList();
		if (moved.Count > 0)
			ThreadPool.QueueUserWorkItem(_ => Guard(() => moved.ForEach(PublishSessionChangedByNode)));

		return new ScheduleChangedPayload(touched.Count == 1 ? touched.First() : null);
	}

	private Dictionary<string, string> ScheduleOwners()
	{
		var owners = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var schedule in _scheduler!.GetSchedules())
			owners.TryAdd(schedule.ScheduleId, schedule.TargetNodeId);
		return owners;
	}

	private void OnSessionFileRewritten(object? sender, SessionFileRewrittenEventArgs e) => Guard(() =>
	{
		var fileName = e.FileName;

		// A new content generation starts: what was counted for the old one no longer applies.
		lock (_entriesGate)
		{
			_pendingAppends.Remove(fileName);
			_publishedSeq.Remove(fileName);
		}

		_bus.PublishCoalesced(
			Constants.Remote.Events.SessionReset, FileKeyPrefix + fileName,
			() => BuildResetPayload(fileName),
			Constants.Remote.Timings.Immediate);
	});

	private void PublishSessionChangedByNode(string nodeId) =>
		_bus.PublishCoalesced(
			Constants.Remote.Events.SessionChanged, NodeKeyPrefix + nodeId,
			() => BuildSessionChangedPayload(_daemon.FindSessionByNodeId(nodeId)),
			Constants.Remote.Timings.SessionChangedWindow);

	private void PublishSessionChangedForSupervisors(SessionNodeModel worker)
	{
		var visited = new HashSet<string>(StringComparer.Ordinal) { worker.NodeId };
		var current = worker;
		while (!string.IsNullOrEmpty(current.SupervisorNodeId) && visited.Add(current.SupervisorNodeId))
		{
			var supervisor = _daemon.FindSessionByNodeId(current.SupervisorNodeId);
			if (supervisor == null)
				return;

			PublishSessionChangedByNode(supervisor.NodeId);
			current = supervisor;
		}
	}

	private SessionChangedPayload? BuildSessionChangedPayload(SessionNodeModel? node) =>
		node == null ? null : new SessionChangedPayload(_states.Build(node));

	private SessionEpochPayload? BuildEpochPayload(string fileName)
	{
		var node = _daemon.FindSessionByFileName(fileName);
		return node == null ? null : new SessionEpochPayload(node.NodeId, _epochs.GetEpoch(fileName));
	}

	private SessionResetPayload? BuildResetPayload(string fileName)
	{
		var node = _daemon.FindSessionByFileName(fileName);
		if (node == null)
			return null;

		// The header read is what rebuilds the index of a rewritten file, so the id published here is the new generation's.
		var header = _store?.GetHeader(fileName);
		var fileId = header is { Exists: true } ? EntryIds.FormatFileId(header.FileId) : null;
		return new SessionResetPayload(node.NodeId, _epochs.GetEpoch(fileName), fileId);
	}

	/// <summary>
	/// <c>entries.appended</c> for everything appended since the last publication of the file. The range starts at the sequence
	/// last published for the same content generation; for the first publication of a generation in this run (or after a
	/// rewrite) it is the number of append notifications counted since then. The entries ride inline when they are few and
	/// small, else the payload carries the id range only.
	/// </summary>
	private EntriesAppendedPayload? BuildEntriesAppendedPayload(string fileName)
	{
		var node = _daemon.FindSessionByFileName(fileName);
		if (node == null || _store == null)
		{
			ForgetFile(fileName); // deleted session: drop its bookkeeping
			return null;
		}

		try
		{
			var header = _store.GetHeader(fileName);
			if (!header.Exists)
			{
				ForgetFile(fileName);
				return null;
			}

			long from;
			long to = header.NextOrdinal;
			lock (_entriesGate)
			{
				var pending = _pendingAppends.GetValueOrDefault(fileName);
				_pendingAppends.Remove(fileName);

				from = _publishedSeq.TryGetValue(fileName, out var mark) && mark.FileId == header.FileId && mark.Seq <= to
					? mark.Seq
					: Math.Max(0, to - pending);

				_publishedSeq[fileName] = (header.FileId, to);
			}

			if (from >= to)
				return null;

			return new EntriesAppendedPayload(
				node.NodeId, EntryIds.FormatFileId(header.FileId), from, to,
				ReadInlineEntries(fileName, header.FileId, from, to));
		}
		catch (Exception ex) when (ex is TdcpException or System.IO.IOException)
		{
			_log.Debug(ex, "RemoteNotificationPublisher: entries.appended skipped for {FileName}", fileName);
			return null;
		}
	}

	private void ForgetFile(string fileName)
	{
		lock (_entriesGate)
		{
			_pendingAppends.Remove(fileName);
			_publishedSeq.Remove(fileName);
		}
	}

	/// <summary>
	/// Before the first append of a content generation is counted, records the sequence just before it (the header's next
	/// ordinal minus the entry that raised this event), so the first <c>fromSeq</c> is exact instead of counted.
	/// </summary>
	private void SeedPublishedSeq(string fileName)
	{
		lock (_entriesGate)
		{
			if (_publishedSeq.ContainsKey(fileName))
				return;
		}

		try
		{
			var header = _store!.GetHeader(fileName);
			if (!header.Exists)
				return;

			lock (_entriesGate)
			{
				if (!_publishedSeq.ContainsKey(fileName))
					_publishedSeq[fileName] = (header.FileId, Math.Max(0, header.NextOrdinal - 1));
			}
		}
		catch (Exception ex) when (ex is TdcpException or System.IO.IOException)
		{
			// The counted fallback in BuildEntriesAppendedPayload still applies.
		}
	}

	/// <summary>The new entries as DTOs when they fit the inline caps; null (ids only) when not or when the file changed meanwhile.</summary>
	private IReadOnlyList<SessionEntryDto>? ReadInlineEntries(string fileName, Guid fileId, long from, long to)
	{
		var count = to - from;
		if (count > Constants.Remote.Limits.EntriesAppendedInlineMaxEntries)
			return null;

		var cap  = Constants.Remote.Limits.EntriesAppendedInlineMaxBytes;
		var page = _store!.ReadPage(fileName, new SessionContentPageRequest(null, (int)from - 1, (int)count, cap, cap));
		if (page.FileId != fileId || page.Entries.Count != count || page.Entries.Any(e => e.Truncated))
			return null;

		return [.. page.Entries.Select(e => e.Entry.ToDto(e.Ordinal, Constants.Remote.Limits.EntryContentMaxChars) with { EntryId = EntryIds.Format(fileId, e.Ordinal) })];
	}

	/// <summary>An event handler must never throw into the thread that raised the event.</summary>
	private static void Guard(Action handler)
	{
		try
		{
			handler();
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "RemoteNotificationPublisher: an event handler failed");
		}
	}
}
