using System;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IProfileSwitchService" />
/// <remarks>
/// <see cref="Switch"/> never moves files under a turn: per node it takes the node's turn lock with <c>Wait(0)</c> and queues the switch when the
/// lock is held or the session is running. <see cref="ApplyPending"/> and <see cref="RecordTurnProfile"/> are called by the turn paths while they
/// already hold that lock, so they do not take it. Created by Claude.
/// </remarks>
public sealed class ProfileSwitchService : IProfileSwitchService
{
	private static readonly ILogger _log = Log.ForContext<ProfileSwitchService>();

	private readonly IAppSettingsService _appSettings;
	private readonly ISessionRegistry _registry;
	private readonly ISessionFileService _files;
	private readonly IProfileResolver _resolver;
	private readonly IClaudeSessionMigrator _migrator;
	private readonly ISessionEngineRouter _engines;
	private readonly Lazy<ISessionTurnService>? _turns;
	private readonly TessynDesktop.Codex.ICodexProfileResolver? _codexProfiles;
	private readonly TessynDesktop.Codex.CodexProfileNaming _codexNaming = new();

	/// <param name="turns">Owner of the per-node turn locks; lazy because the turn service itself uses this service. Null = only the running flag is checked.</param>
	public ProfileSwitchService(
		IAppSettingsService appSettings,
		ISessionRegistry registry,
		ISessionFileService files,
		IProfileResolver resolver,
		IClaudeSessionMigrator migrator,
		ISessionEngineRouter engines,
		Lazy<ISessionTurnService>? turns = null,
		TessynDesktop.Codex.ICodexProfileResolver? codexProfiles = null)
	{
		_codexProfiles = codexProfiles;
		_appSettings = appSettings;
		_registry    = registry;
		_files       = files;
		_resolver    = resolver;
		_migrator    = migrator;
		_engines   = engines;
		_turns       = turns;
	}

	/// <summary>What moving one session did: the old-config-dir paths found, the files that failed and whether the pin was written.</summary>
	private sealed record ApplyOutcome(List<string> Paths, List<string> Failed, bool Pinned)
	{
		public bool Complete => Pinned && Failed.Count == 0;
	}

	/// <inheritdoc />
	public ProfileSwitchResultDto Switch(string nodeId, string profileId, bool includeDescendants)
	{
		var node   = _registry.FindModelByNodeId(nodeId) ?? throw new ProfileSwitchException("Session not found");
		if (_codexNaming.IsCodexProfileId(profileId))
			return SwitchCodex(node, profileId);

		var target = _resolver.FindById(profileId) ?? throw new ProfileSwitchException($"Profile not found: {profileId}");

		var paths     = new List<string>();
		var moved     = new List<ProfileSwitchedSessionDto>();
		var queued    = new List<ProfileSwitchedSessionDto>();
		var failed    = new List<ProfileSwitchFailureDto>();
		var nodeMoved = false;

		var sessions = new[] { node }.Concat(includeDescendants ? GetDescendants(nodeId) : []).ToList();
		foreach (var session in sessions)
		{
			var from = EffectiveProfile(session);
			var dto  = new ProfileSwitchedSessionDto(session.NodeId, session.Name, from.ProfileId, from.DisplayName);
			var isTarget = session.NodeId == node.NodeId;

			if (from.ProfileId == target.ProfileId && from.IsResolved)
			{
				Pin(session, target); // already there: record the pin, move nothing
				session.PendingProfileId = null; // a switch back cancels an earlier queued switch (ISSUE.013 N2)
				continue;
			}

			var outcome = TryApplyUnderTurnLock(session, target);
			if (outcome == null)
			{
				session.PendingProfileId = target.ProfileId;
				queued.Add(dto);
				_log.Information("Profile switch of session {NodeId} to {Profile} is queued until its turn ends", session.NodeId, target.DisplayName);
				continue;
			}

			paths.AddRange(outcome.Paths);
			if (outcome.Failed.Count > 0)
				failed.Add(new ProfileSwitchFailureDto(session.NodeId, session.Name, outcome.Failed));
			if (!outcome.Complete)
				continue;

			if (isTarget)
				nodeMoved = true;
			else
				moved.Add(dto);
		}

		Persist(sessions);
		return new ProfileSwitchResultDto(
			target.ProfileId, target.DisplayName, nodeMoved, moved, queued, paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), failed);
	}

	/// <summary>Moves the session while holding its turn lock; null when the session is running or its lock is held (the caller queues the switch).</summary>
	private ApplyOutcome? TryApplyUnderTurnLock(SessionNodeModel session, ProfileResolution target)
	{
		if (_registry.IsSessionRunning(session.NodeId))
			return null;

		var turnLock = _turns?.Value.GetTurnLock(session.NodeId);
		if (turnLock != null && !turnLock.Wait(0))
			return null;

		try
		{
			// A turn that started between the two checks has set the running flag by now.
			if (_registry.IsSessionRunning(session.NodeId))
				return null;

			return Apply(session, target);
		}
		finally
		{
			turnLock?.Release();
		}
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

		if (_codexNaming.IsCodexProfileId(node.PendingProfileId))
		{
			var choice = _codexProfiles?.Find(node.PendingProfileId);
			node.PendingProfileId = null;
			if (choice?.Profile is not null)
				ApplyCodex(node, choice);
			Persist([node]);
			return choice?.Profile is not null;
		}

		var target = _resolver.FindById(node.PendingProfileId);
		if (target == null)
		{
			_log.Warning("Queued profile switch of session {NodeId} dropped: the profile no longer exists", node.NodeId);
			node.PendingProfileId = null;
			Persist([node]); // the queued switch is gone: open clients drop its "switching to" mark
			return false;
		}

		// Always run the (idempotent) move: a switch that failed half way is retried here.
		var outcome = Apply(node, target);
		Persist([node]);
		return outcome.Complete;
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
			Persist([node]); // a changed pin is part of the session state: publish it
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

	/// <summary>
	/// Moves the session's files into the target store; when the transcript did not move the pin is not written. Failed files are collected and
	/// the switch stays queued (<see cref="SessionNodeModel.PendingProfileId"/>) so the next turn start retries it. The caller holds the turn lock.
	/// </summary>
	private ApplyOutcome Apply(SessionNodeModel session, ProfileResolution target)
	{
		var paths  = new List<string>();
		var failed = new List<string>();
		var transcriptOk = true;
		var ids   = new[] { session.ClaudeSessionId }.Concat(session.PriorClaudeSessionIds)
			.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal);

		foreach (var id in ids)
		{
			var report = _migrator.Migrate(session.WorkingDirectory, id!, target.ConfigDir);
			paths.AddRange(report.OldConfigPaths);
			failed.AddRange(report.Failed);
			if (report.HasFailures && !report.Found)
				transcriptOk = false;
		}

		if (!transcriptOk)
		{
			session.PendingProfileId = target.ProfileId;
			_log.Warning("Profile switch of session {NodeId} to {Profile} failed: the transcript could not be moved ({Count} file(s)); the switch stays queued",
				session.NodeId, target.DisplayName, failed.Count);
			return new ApplyOutcome(paths, failed, Pinned: false);
		}

		if (_engines.IsAlive(session.NodeId))
		{
			_engines.Stop(session.NodeId); // R7: the next launch builds its prompt and memory path from the new config dir
			_log.Information("Stopped the streaming process of session {NodeId} for its profile switch", session.NodeId);
		}

		Pin(session, target);
		session.PendingProfileId = failed.Count > 0 ? target.ProfileId : null;
		if (failed.Count > 0)
			_log.Warning("Session {NodeId} is on profile {Profile}, but {Count} file(s) of its session folder could not be moved; the move is retried at its next turn", session.NodeId, target.DisplayName, failed.Count);
		else
			_log.Information("Switched session {NodeId} to profile {Profile}", session.NodeId, target.DisplayName);
		return new ApplyOutcome(paths, failed, Pinned: true);
	}

	private static void Pin(SessionNodeModel session, ProfileResolution target)
	{
		session.ProfileId   = target.ProfileId;
		session.ProfileName = target.IsDefault ? null : target.DisplayName;
	}

	/// <summary>
	/// FR.33.5: a switch to a ChatGPT profile. Threads are not moved between Codex homes (version 1): the session's thread is detached, so the
	/// next turn starts a new thread on the target profile seeded from the transcript; no file moves. A running session queues the switch.
	/// </summary>
	private ProfileSwitchResultDto SwitchCodex(SessionNodeModel node, string profileId)
	{
		var target = _codexProfiles?.Find(profileId) ?? throw new ProfileSwitchException($"Profile not found: {profileId}");
		var dto    = new ProfileSwitchedSessionDto(node.NodeId, node.Name, node.ProfileId ?? Constants.ProfileSwitch.DefaultProfileId, node.ProfileName ?? node.ProfileId ?? Constants.ProfileSwitch.DefaultProfileName);

		var turnLock = _turns?.Value.GetTurnLock(node.NodeId);
		if (_registry.IsSessionRunning(node.NodeId) || (turnLock != null && !turnLock.Wait(0)))
		{
			node.PendingProfileId = target.Profile!.ProfileId;
			Persist([node]);
			return new ProfileSwitchResultDto(target.Profile.ProfileId, target.DisplayName, false, [], [dto], [], []);
		}

		try
		{
			ApplyCodex(node, target);
			node.PendingProfileId = null;
		}
		finally
		{
			turnLock?.Release();
		}

		Persist([node]);
		return new ProfileSwitchResultDto(target.Profile!.ProfileId, target.DisplayName, true, [], [], [], []);
	}

	private void ApplyCodex(SessionNodeModel node, TessynDesktop.Codex.CodexProfileChoice target)
	{
		if (string.Equals(node.ProfileId, target.Profile!.ProfileId, StringComparison.OrdinalIgnoreCase))
			return;

		var codex = _engines.Engines.FirstOrDefault(e => e.EngineId == Constants.SessionEngines.Codex);
		if (codex?.DetachConversation(node) == true)
			_log.Information("Profile switch of Codex session {NodeId} to {Profile}: its thread is detached and the next turn is seeded from the transcript", node.NodeId, target.DisplayName);

		lock (_appSettings.SyncRoot)
		{
			node.ProfileId   = target.Profile.ProfileId;
			node.ProfileName = target.DisplayName;
		}
	}

	private void Persist(IEnumerable<SessionNodeModel> sessions)
	{
		_appSettings.Save();
		foreach (var session in sessions)
			_registry.RaiseSessionSettingsChanged(session.NodeId);
	}
}
