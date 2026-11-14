using System;
using System.Collections.Generic;
using System.Linq;
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
	private const string NodeKeyPrefix = "node:";
	private const string FileKeyPrefix = "file:";
	private const string AllNodesKey = "*";

	private readonly IDaemonClient _daemon;
	private readonly ISessionFileService _files;
	private readonly IRemoteEventBus _bus;
	private readonly ITreeVersionTracker _treeVersion;
	private readonly ISessionEpochTracker _epochs;
	private readonly ISessionStateBuilder _states;

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
		ISessionContentStore? store = null)
	{
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
		}
	}

	private void OnTreeChanged(object? sender, TreeChangedEventArgs e) => Guard(() =>
	{
		if (e.Kind == TreeChangeKind.SessionIsRunningChanged)
		{
			if (e.Session != null)
				PublishSessionChangedByNode(e.Session.NodeId);
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
				return new TurnCompletedPayload(node.NodeId, cancelled, error);
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
			return null;

		try
		{
			var header = _store.GetHeader(fileName);
			if (!header.Exists)
				return null;

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
