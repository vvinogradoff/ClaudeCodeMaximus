using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// Remote methods of the session tree: <c>tree.snapshot</c>, <c>tree.workers</c>, <c>tree.search</c> and
/// <c>tree.createSession</c> (contract 3.2–3.5). Reads copy the tree under the settings lock; live models are never serialized.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TreeRemoteRpcModule : IRemoteRpcModule
{
	private static readonly ILogger _log = Log.ForContext<TreeRemoteRpcModule>();

	private readonly IRemoteTreeReader _reader;
	private readonly ISessionRegistry _registry;
	private readonly ISessionStateBuilder _states;
	private readonly ISessionActivityService _activity;
	private readonly ITreeVersionTracker _version;
	private readonly IDirectoryLabelService _labels;
	private readonly IClaudeSessionIndexService _index;
	private readonly ISessionSearchService _fileSearch;

	public TreeRemoteRpcModule(
		IRemoteTreeReader reader,
		ISessionRegistry registry,
		ISessionStateBuilder states,
		ISessionActivityService activity,
		ITreeVersionTracker version,
		IDirectoryLabelService labels,
		IClaudeSessionIndexService index,
		ISessionSearchService fileSearch)
	{
		_reader     = reader;
		_registry   = registry;
		_states     = states;
		_activity   = activity;
		_version    = version;
		_labels     = labels;
		_index      = index;
		_fileSearch = fileSearch;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry)
	{
		registry.AddSync(Constants.Remote.Methods.TreeSnapshot,      Snapshot,      remoteCallable: true);
		registry.AddSync(Constants.Remote.Methods.TreeWorkers,       Workers,       remoteCallable: true);
		registry.Add    (Constants.Remote.Methods.TreeSearch,        SearchAsync,   remoteCallable: true);
		registry.AddSync(Constants.Remote.Methods.TreeCreateSession, CreateSession, remoteCallable: true);
	}

	// ── tree.snapshot / tree.workers ──────────────────────────────────────────────

	private object? Snapshot(JsonNode? @params)
	{
		var since = TdcpParams.OptionalLong(@params, "sinceVersion");

		// Read the version BEFORE building: a change during the build then leaves the client one version behind, never ahead.
		var version = _version.Current;
		if (since.HasValue && since.Value == version)
			return new TreeSnapshotResult(version, true, null);

		var snapshot     = _reader.Snapshot();
		var workerCounts = snapshot.CountWorkers();

		var directories = new List<TreeDirectoryDto>(snapshot.Directories.Count);
		foreach (var dir in snapshot.Directories)
		{
			directories.Add(new TreeDirectoryDto(
				dir.Id,
				_labels.GetLabel(dir.Path),
				dir.IsExpanded,
				MapGroups(dir.Groups, snapshot, workerCounts),
				MapSessions(dir.Sessions, snapshot, workerCounts)));
		}

		return new TreeSnapshotResult(version, null, directories);
	}

	private List<TreeGroupDto> MapGroups(IReadOnlyList<RemoteGroupRecord> groups, RemoteTreeSnapshotModel snapshot, IReadOnlyDictionary<string, int> workerCounts)
	{
		var result = new List<TreeGroupDto>(groups.Count);
		foreach (var g in groups)
			result.Add(new TreeGroupDto(g.Id, g.Name, g.IsExpanded, MapGroups(g.Groups, snapshot, workerCounts), MapSessions(g.Sessions, snapshot, workerCounts)));
		return result;
	}

	private List<TreeSessionDto> MapSessions(IReadOnlyList<RemoteSessionRecord> sessions, RemoteTreeSnapshotModel snapshot, IReadOnlyDictionary<string, int> workerCounts)
	{
		var result = new List<TreeSessionDto>();
		foreach (var s in sessions)
		{
			if (!snapshot.IsWorker(s))
				result.Add(MapSession(s, workerCounts, supervisorId: null));
		}
		return result;
	}

	private TreeSessionDto MapSession(RemoteSessionRecord s, IReadOnlyDictionary<string, int> workerCounts, string? supervisorId) =>
		new(s.NodeId, s.Name, workerCounts.GetValueOrDefault(s.NodeId), supervisorId, _states.Build(s.Node));

	private object? Workers(JsonNode? @params)
	{
		var supervisorId = TdcpParams.RequireString(@params, "supervisorId");

		var snapshot = _reader.Snapshot();
		if (snapshot.AllSessions.All(s => s.NodeId != supervisorId))
			throw new TdcpException("Session not found");

		var workerCounts = snapshot.CountWorkers();
		var workers = snapshot.AllSessions
			.Where(s => s.SupervisorNodeId == supervisorId && s.NodeId != supervisorId)
			.Select(s => MapSession(s, workerCounts, supervisorId))
			.ToList();

		return new TreeWorkersResult(supervisorId, workers);
	}

	// ── tree.search ───────────────────────────────────────────────────────────────

	/// <summary>FR.1.14 — queries starting with <c>https://</c> or shorter than 8 characters search in exact mode unless the client says otherwise.</summary>
	internal static bool IsAutoExactQuery(string trimmedQuery) =>
		trimmedQuery.Length > 0
		&& (trimmedQuery.Length < Constants.TreeSearch.AutoExactBelowLength
			|| trimmedQuery.StartsWith(Constants.TreeSearch.AutoExactPrefix, StringComparison.OrdinalIgnoreCase));

	private async Task<object?> SearchAsync(JsonNode? @params, CancellationToken ct)
	{
		var query = TdcpParams.RequireString(@params, "query").Trim();
		var exact = TdcpParams.OptionalBool(@params, "exact");
		var scope = TdcpParams.OptionalString(@params, "scopeNodeId");

		var autoExact = IsAutoExactQuery(query);
		if (query.Length == 0)
			return new TreeSearchResult(query, false, false, false, []);

		var effectiveExact = exact ?? autoExact;
		var snapshot       = _reader.Snapshot();

		string? projectSlug = null;
		if (!string.IsNullOrEmpty(scope))
		{
			var scopeSession = snapshot.AllSessions.FirstOrDefault(s => s.NodeId == scope)
				?? throw new TdcpException("Session not found");
			projectSlug = Constants.ClaudeSessions.BuildProjectSlug(scopeSession.WorkingDirectory);
		}

		Func<RemoteSessionRecord, SessionSearchScoreModel?> scoreOf;
		var usedFallback = false;
		try
		{
			// Unranked, uncapped membership test with per-session counts (ISSUE.007): not the ranked top-N of SearchSessionsAsync.
			var scores = await _index.SearchSessionScoresAsync(query, projectSlug, ct);
			var byKey  = new Dictionary<string, SessionSearchScoreModel>(scores.Count, StringComparer.Ordinal);
			foreach (var score in scores.Where(score => !effectiveExact || score.StrictMatchCount > 0))
				byKey[score.ExternalId] = score;

			scoreOf = s => s.ExternalKey != null && byKey.TryGetValue(s.ExternalKey, out var score) ? score : null;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Tree search: the index query failed, falling back to the file scan");
			usedFallback = true;
			scoreOf      = FallbackScorer(query);
		}

		var byId    = snapshot.AllSessions.ToDictionary(s => s.NodeId, StringComparer.Ordinal);
		var matches = TreeSearchRanker.Rank(snapshot.AllSessions, scoreOf, nodeId => byId.TryGetValue(nodeId, out var r) ? _activity.GetActivity(r.Node).LastPromptUtc : null);
		return new TreeSearchResult(query, effectiveExact, autoExact, usedFallback, matches);
	}

	/// <summary>File-body scan without scores (every hit counts as one loose match, exactly as on the desktop, so Exact does not filter it).</summary>
	private Func<RemoteSessionRecord, SessionSearchScoreModel?> FallbackScorer(string query)
	{
		try
		{
			var files = _fileSearch.FindMatchingSessionFiles(query);
			return s => files.Contains(s.Node.FileName)
				? new SessionSearchScoreModel { ExternalId = s.Node.FileName, StrictMatchCount = 0, LooseMatchCount = 1 }
				: null;
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Tree search: the file-scan fallback failed too");
			throw new TdcpException("Search is unavailable");
		}
	}

	// ── tree.createSession ────────────────────────────────────────────────────────

	private object? CreateSession(JsonNode? @params)
	{
		var anchorId = TdcpParams.RequireString(@params, "anchorId");
		var name     = TdcpParams.RequireString(@params, "name").Trim();
		if (name.Length == 0)
			throw new TdcpInvalidParamsException("'name' must not be blank");

		var placement = _registry.CreateSessionAfter(anchorId, name)
			?? throw new TdcpException("Anchor not found");

		var session = new TreeSessionDto(placement.Session.NodeId, placement.Session.Name, 0, null, _states.Build(placement.Session));
		return new CreateSessionResult(session, placement.Directory.NodeId, placement.Group?.NodeId);
	}
}
