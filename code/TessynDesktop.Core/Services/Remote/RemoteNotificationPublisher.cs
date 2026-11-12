using System;
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

	public RemoteNotificationPublisher(
		IDaemonClient daemon,
		ISessionFileService files,
		IRemoteEventBus bus,
		ITreeVersionTracker treeVersion,
		ISessionEpochTracker epochs,
		ISessionStateBuilder states)
	{
		_daemon      = daemon;
		_files       = files;
		_bus         = bus;
		_treeVersion = treeVersion;
		_epochs      = epochs;
		_states      = states;
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

		// Every completion is its own notification (a unique key), but the node lookup is still deferred off this thread.
		_bus.PublishCoalesced(
			Constants.Remote.Events.TurnCompleted, Guid.NewGuid().ToString("N"),
			() =>
			{
				var node = _daemon.FindSessionByFileName(fileName);
				return node == null ? null : new TurnCompletedPayload(node.NodeId, cancelled, error);
			},
			Constants.Remote.Timings.Immediate);
	});

	private void OnAttentionChanged(object? sender, AttentionChangedEventArgs e) => Guard(() =>
	{
		var nodeId = e.NodeId;

		_bus.PublishCoalesced(
			Constants.Remote.Events.AttentionChanged, NodeKeyPrefix + nodeId,
			() => new AttentionChangedPayload(nodeId, new AttentionSummaryDto(_daemon.GetAttentionSummary(nodeId))),
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

		_bus.PublishCoalesced(
			Constants.Remote.Events.SessionReset, FileKeyPrefix + fileName,
			() => BuildEpochPayload(fileName),
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
