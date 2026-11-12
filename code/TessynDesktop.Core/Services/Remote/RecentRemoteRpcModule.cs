using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Remote methods of the Recent dropdown: <c>recent.list</c> and <c>recent.hide</c> (contract 3.6, 3.7; FR.9.6–FR.9.8).
/// The membership and ordering rules are the dropdown's, computed over models, session activity, schedules and attention summaries.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class RecentRemoteRpcModule : IRemoteRpcModule
{
	private readonly IRemoteTreeReader _reader;
	private readonly ISessionStateBuilder _states;
	private readonly ISessionActivityService _activity;
	private readonly IAttentionService _attention;
	private readonly ISchedulerService _scheduler;
	private readonly IDirectoryLabelService _labels;
	private readonly ISessionRegistry _registry;
	private readonly IAppSettingsService _appSettings;

	public RecentRemoteRpcModule(
		IRemoteTreeReader reader,
		ISessionStateBuilder states,
		ISessionActivityService activity,
		IAttentionService attention,
		ISchedulerService scheduler,
		IDirectoryLabelService labels,
		ISessionRegistry registry,
		IAppSettingsService appSettings)
	{
		_reader      = reader;
		_states      = states;
		_activity    = activity;
		_attention   = attention;
		_scheduler   = scheduler;
		_labels      = labels;
		_registry    = registry;
		_appSettings = appSettings;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(Constants.Remote.Methods.RecentList, List,  remoteCallable: true);
		registry.AddSync(Constants.Remote.Methods.RecentHide, Hide,  remoteCallable: true);
	}

	private object? List(JsonNode? @params)
	{
		var now      = DateTimeOffset.UtcNow;
		var snapshot = _reader.Snapshot();
		var scheduled = ScheduledNodeIds();

		var labelByDirectory = snapshot.Directories.ToDictionary(d => d.Id, d => _labels.GetLabel(d.Path), StringComparer.Ordinal);

		var rows = new List<(RemoteSessionRecord Session, DateTimeOffset? Activity, bool HasSchedules, bool Pinned)>();
		foreach (var session in snapshot.TopLevelSessions())
		{
			var lastActivity = _activity.GetActivity(session.Node).LastActivityUtc;
			var hasSchedules = scheduled.Contains(session.NodeId);
			var pinned       = IsPinned(session, hasSchedules);

			var show = RecentSessionVisibilityService.ShouldShowInRecentDropdown(
				session.IsFavorite,
				HasOpenAttention(session.NodeId),
				hasSchedules,
				RecentSessionVisibilityService.IsRecentlyActive(lastActivity, now),
				RecentSessionVisibilityService.IsHiddenFromRecent(session.RecentIgnoredAt, lastActivity));
			if (show)
				rows.Add((session, lastActivity, hasSchedules, pinned));
		}

		// Newest first, sessions without activity last; OrderBy is stable, so ties keep tree order.
		var ordered = rows
			.OrderByDescending(r => r.Activity.HasValue)
			.ThenByDescending(r => r.Activity ?? DateTimeOffset.MinValue)
			.Select(r => new RecentRowDto(
				r.Session.NodeId,
				r.Session.Name,
				labelByDirectory.GetValueOrDefault(r.Session.DirectoryId, string.Empty),
				_states.Build(r.Session.Node),
				r.HasSchedules,
				r.Pinned,
				CanHide: !r.Pinned))
			.ToList();

		return new RecentListResult(now, ordered);
	}

	private object? Hide(JsonNode? @params)
	{
		var nodeId = TdcpParams.RequireString(@params, "nodeId");

		var snapshot = _reader.Snapshot();
		var session  = snapshot.Find(nodeId) ?? throw new TdcpException("Session not found");

		// A pinned session stays listed whatever its hidden state, so hiding it would have no effect: refuse (FR.9.8).
		if (IsPinned(session, ScheduledNodeIds().Contains(nodeId)))
			return new RecentHideResult(false);

		lock (_appSettings.SyncRoot)
		{
			var live = _registry.FindModelByNodeId(nodeId);
			if (live == null)
				throw new TdcpException("Session not found");

			live.RecentIgnoredAt = DateTimeOffset.UtcNow;
			_appSettings.Save();
		}

		return new RecentHideResult(true);
	}

	private bool IsPinned(RemoteSessionRecord session, bool hasSchedules) =>
		RecentSessionVisibilityService.IsPinnedInRecentDropdown(session.IsFavorite, HasOpenAttention(session.NodeId), hasSchedules);

	/// <summary>Any open item counts, Decided included (FR.9.8, FR.23.17).</summary>
	private bool HasOpenAttention(string nodeId) => _attention.GetSummary(nodeId).AllOpenCount > 0;

	private HashSet<string> ScheduledNodeIds() =>
		_scheduler.GetSchedules().Select(s => s.TargetNodeId).ToHashSet(StringComparer.Ordinal);
}
