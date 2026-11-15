using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IProfileSwitchService" />
/// <remarks>Created by Claude</remarks>
public sealed class ProfileSwitchService : IProfileSwitchService
{
	private static readonly ILogger _log = Log.ForContext<ProfileSwitchService>();

	private readonly IAppSettingsService _appSettings;
	private readonly ISessionRegistry _registry;
	private readonly ISessionFileService _files;
	private readonly IProfileResolver _resolver;
	private readonly IClaudeSessionMigrator _migrator;
	private readonly IClaudeStreamingSessionManager _streaming;

	public ProfileSwitchService(
		IAppSettingsService appSettings,
		ISessionRegistry registry,
		ISessionFileService files,
		IProfileResolver resolver,
		IClaudeSessionMigrator migrator,
		IClaudeStreamingSessionManager streaming)
	{
		_appSettings = appSettings;
		_registry    = registry;
		_files       = files;
		_resolver    = resolver;
		_migrator    = migrator;
		_streaming   = streaming;
	}

	/// <inheritdoc />
	public ProfileSwitchResultDto Switch(string nodeId, string profileId, bool includeDescendants)
	{
		var node   = _registry.FindModelByNodeId(nodeId) ?? throw new ProfileSwitchException("Session not found");
		var target = _resolver.FindById(profileId) ?? throw new ProfileSwitchException($"Profile not found: {profileId}");

		var paths   = new List<string>();
		var moved   = new List<ProfileSwitchedSessionDto>();
		var queued  = new List<ProfileSwitchedSessionDto>();
		var nodeMoved = false;

		var sessions = new[] { node }.Concat(includeDescendants ? GetDescendants(nodeId) : []).ToList();
		foreach (var session in sessions)
		{
			var from = EffectiveProfile(session);
			var dto  = new ProfileSwitchedSessionDto(session.NodeId, session.Name, from.ProfileId, from.DisplayName);
			var isTarget = session.NodeId == node.NodeId;

			if (_registry.IsSessionRunning(session.NodeId))
			{
				session.PendingProfileId = target.ProfileId;
				queued.Add(dto);
				_log.Information("Profile switch of running session {NodeId} to {Profile} is queued until its turn ends", session.NodeId, target.DisplayName);
				continue;
			}

			if (from.ProfileId == target.ProfileId && from.IsResolved)
			{
				Pin(session, target); // already there: record the pin, move nothing
				continue;
			}

			paths.AddRange(Apply(session, target));
			if (isTarget)
				nodeMoved = true;
			else
				moved.Add(dto);
		}

		Persist(sessions);
		return new ProfileSwitchResultDto(target.ProfileId, target.DisplayName, nodeMoved, moved, queued, paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
	}

	/// <inheritdoc />
	public IReadOnlyList<SessionNodeModel> GetDescendants(string nodeId)
	{
		var children = _registry.SnapshotSessionsForListing()
			.Where(s => !string.IsNullOrEmpty(s.SupervisorNodeId))
			.ToLookup(s => s.SupervisorNodeId!, StringComparer.Ordinal);

		var result  = new List<SessionNodeModel>();
		var seen    = new HashSet<string>(StringComparer.Ordinal) { nodeId };
		var pending = new Queue<string>();
		pending.Enqueue(nodeId);

		while (pending.Count > 0)
		{
			foreach (var child in children[pending.Dequeue()])
			{
				if (!seen.Add(child.NodeId))
					continue; // a supervisor cycle must not loop

				var model = _registry.FindModelByNodeId(child.NodeId);
				if (model != null)
					result.Add(model);
				pending.Enqueue(child.NodeId);
			}
		}

		return result;
	}

	/// <inheritdoc />
	public bool IsDescendantOf(string nodeId, string ancestorNodeId) =>
		GetDescendants(ancestorNodeId).Any(d => d.NodeId == nodeId);

	/// <inheritdoc />
	public bool ApplyPending(SessionNodeModel node)
	{
		if (string.IsNullOrEmpty(node.PendingProfileId))
			return false;

		var target = _resolver.FindById(node.PendingProfileId);
		node.PendingProfileId = null;
		if (target == null)
		{
			_log.Warning("Queued profile switch of session {NodeId} dropped: the profile no longer exists", node.NodeId);
			_appSettings.Save();
			return false;
		}

		if (EffectiveProfile(node).ProfileId != target.ProfileId)
			Apply(node, target);
		else
			Pin(node, target);

		Persist([node]);
		return true;
	}

	/// <inheritdoc />
	public void RecordTurnProfile(SessionNodeModel node, string? displayName)
	{
		var target = string.IsNullOrEmpty(displayName) ? _resolver.FindById(Constants.ProfileSwitch.DefaultProfileId) : _resolver.FindByName(displayName);
		if (target == null)
			return; // a name that matches no profile is not recorded as a pin (the resolver reports it)

		var current = _resolver.DescribePin(node);
		if (current != null && current.ProfileId != target.ProfileId)
		{
			Apply(node, target);
			Persist([node]);
			return;
		}

		if (node.ProfileId != target.ProfileId)
		{
			Pin(node, target);
			_appSettings.Save();
		}
	}

	/// <summary>The profile the session would run on now: its pin, else its last recorded profile, else Default.</summary>
	private ProfileResolution EffectiveProfile(SessionNodeModel node)
	{
		var recorded = node.ProfileName;
		if (string.IsNullOrEmpty(node.ProfileId) && string.IsNullOrEmpty(recorded))
		{
			try { recorded = LastUsedSessionSettingsReader.Read(_files, node.FileName).ProfileName; }
			catch (Exception ex) { _log.Debug(ex, "Could not read the last used profile of {NodeId}", node.NodeId); }
		}

		return _resolver.Resolve(node, recorded);
	}

	/// <summary>Moves the session's files into the target store, stops a live streaming process, writes the pin. Returns the old-config-dir paths found.</summary>
	private IEnumerable<string> Apply(SessionNodeModel session, ProfileResolution target)
	{
		var paths = new List<string>();
		var ids   = new[] { session.ClaudeSessionId }.Concat(session.PriorClaudeSessionIds)
			.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal);

		foreach (var id in ids)
			paths.AddRange(_migrator.Migrate(session.WorkingDirectory, id!, target.ConfigDir).OldConfigPaths);

		if (_streaming.IsAlive(session.NodeId))
		{
			_streaming.Stop(session.NodeId); // R7: the next launch builds its prompt and memory path from the new config dir
			_log.Information("Stopped the streaming process of session {NodeId} for its profile switch", session.NodeId);
		}

		Pin(session, target);
		session.PendingProfileId = null;
		_log.Information("Switched session {NodeId} to profile {Profile}", session.NodeId, target.DisplayName);
		return paths;
	}

	private static void Pin(SessionNodeModel session, ProfileResolution target)
	{
		session.ProfileId   = target.ProfileId;
		session.ProfileName = target.IsDefault ? null : target.DisplayName;
	}

	private void Persist(IEnumerable<SessionNodeModel> sessions)
	{
		_appSettings.Save();
		foreach (var session in sessions)
			_registry.RaiseSessionSettingsChanged(session.NodeId);
	}
}
