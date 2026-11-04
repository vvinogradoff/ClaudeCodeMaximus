using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Default <see cref="ISessionTagService"/>: declared tags and the Linear workspace map are loaded lazily from
/// <see cref="SessionTagStore"/>, cached, and mutated under one lock; events fire after it is released.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagService : ISessionTagService
{
	private static readonly ILogger _log = Log.ForContext<SessionTagService>();

	private readonly SessionTagStore  _store;
	private readonly SessionTagParser _parser = new();
	private readonly TimeProvider     _time;

	private readonly object _lock = new();
	private readonly Dictionary<string, List<SessionTagModel>> _declared = new();
	private readonly HashSet<string> _guidanceGiven = new();
	private Dictionary<string, string>? _workspaces;

	public event EventHandler<SessionTagsChangedEventArgs>? SessionTagsChanged;

	public SessionTagService(SessionTagStore store) : this(store, TimeProvider.System)
	{
	}

	/// <summary>Constructor for testing: allows a controllable clock.</summary>
	public SessionTagService(SessionTagStore store, TimeProvider time)
	{
		_store = store;
		_time  = time;
	}

	public SessionTagEditResultModel Edit(SessionNodeModel node, IReadOnlyList<string> add, IReadOnlyList<string> remove)
	{
		var outcomes = new List<SessionTagEntryOutcomeModel>();
		var added    = new List<string>();
		var removed  = new List<string>();
		var mapChanged = false;
		IReadOnlyList<SessionTagModel> tags;

		lock (_lock)
		{
			var declared   = LoadDeclaredLocked(node.NodeId);
			var workspaces = LoadWorkspacesLocked();

			foreach (var raw in add)
			{
				var entry = raw.Trim();
				var parse = _parser.Parse(entry, workspaces);
				if (parse.Outcome == SessionTagParseOutcome.Empty)
					continue;

				// FR.24.3: a full Linear URL teaches the map, even when the tag itself is already there.
				if (parse.LearnedTeam != null && parse.LearnedWorkspace != null
				    && (!workspaces.TryGetValue(parse.LearnedTeam, out var known) || known != parse.LearnedWorkspace))
				{
					workspaces[parse.LearnedTeam] = parse.LearnedWorkspace;
					mapChanged = true;
				}

				if (parse.Outcome == SessionTagParseOutcome.Invalid)
				{
					outcomes.Add(new SessionTagEntryOutcomeModel { Kind = SessionTagEntryOutcomeKind.NotTaggedInvalid, Entry = entry });
					continue;
				}

				if (parse.Outcome == SessionTagParseOutcome.UnknownTeam)
				{
					outcomes.Add(new SessionTagEntryOutcomeModel
					{
						Kind = SessionTagEntryOutcomeKind.NotTaggedUnknownTeam, Entry = entry,
						UnknownTeam = parse.UnknownTeam, UnknownIssueKey = parse.UnknownIssueKey,
					});
					continue;
				}

				var tag      = parse.Tag!;
				var existing = FindByKey(DeriveNameTags(node.Name, workspaces), tag.Key) ?? FindByKey(declared, tag.Key);
				if (existing != null)
				{
					outcomes.Add(new SessionTagEntryOutcomeModel { Kind = SessionTagEntryOutcomeKind.AlreadyTagged, Entry = entry, Tag = Copy(existing) });
					continue;
				}

				tag.Source   = SessionTagSource.Session;
				tag.AddedUtc = _time.GetUtcNow();
				declared.Add(tag);
				added.Add(tag.Label);
				outcomes.Add(new SessionTagEntryOutcomeModel { Kind = SessionTagEntryOutcomeKind.Tagged, Entry = entry, Tag = Copy(tag) });
			}

			foreach (var raw in remove)
			{
				var entry = raw.Trim();
				var parse = _parser.Parse(entry, workspaces);
				if (parse.Outcome == SessionTagParseOutcome.Empty)
					continue;

				if (parse.Outcome == SessionTagParseOutcome.Invalid)
				{
					outcomes.Add(new SessionTagEntryOutcomeModel { Kind = SessionTagEntryOutcomeKind.NotRemovedInvalid, Entry = entry });
					continue;
				}

				// A short key of an unknown team cannot be tagged, so it is simply "not tagged".
				var key = parse.Tag?.Key;
				if (key == null)
				{
					outcomes.Add(new SessionTagEntryOutcomeModel { Kind = SessionTagEntryOutcomeKind.NotRemovedNotTagged, Entry = entry });
					continue;
				}

				if (FindByKey(DeriveNameTags(node.Name, workspaces), key) != null)
				{
					outcomes.Add(new SessionTagEntryOutcomeModel { Kind = SessionTagEntryOutcomeKind.NotRemovedFromName, Entry = entry });
					continue;
				}

				var existing = FindByKey(declared, key);
				if (existing == null)
				{
					outcomes.Add(new SessionTagEntryOutcomeModel { Kind = SessionTagEntryOutcomeKind.NotRemovedNotTagged, Entry = entry });
					continue;
				}

				declared.Remove(existing);
				removed.Add(existing.Label);
				outcomes.Add(new SessionTagEntryOutcomeModel { Kind = SessionTagEntryOutcomeKind.Removed, Entry = entry, Tag = Copy(existing) });
			}

			if (added.Count > 0 || removed.Count > 0)
				Persist(() => _store.SaveTags(node.NodeId, declared), () => _declared.Remove(node.NodeId));
			if (mapChanged)
				Persist(() => _store.SaveWorkspaces(workspaces), () => _workspaces = null);

			tags = Merge(declared, node.Name, workspaces);
		}

		// FR.24.3 / brief section 6 item 4: learning covers every session (the caller included); otherwise the node.
		if (mapChanged)
			RaiseChanged(null);
		else if (added.Count > 0 || removed.Count > 0)
			RaiseChanged(node.NodeId);

		return new SessionTagEditResultModel { Outcomes = outcomes, AddedLabels = added, RemovedLabels = removed, Tags = tags };
	}

	public IReadOnlyList<SessionTagModel> GetTags(string nodeId, string? sessionName)
	{
		lock (_lock)
			return Merge(LoadDeclaredLocked(nodeId), sessionName, LoadWorkspacesLocked());
	}

	public bool TakeTagGuidanceDue(string nodeId)
	{
		lock (_lock)
			return _guidanceGiven.Add(nodeId);
	}

	// ── Helpers ────────────────────────────────────────────────────────────────

	private List<SessionTagModel> LoadDeclaredLocked(string nodeId)
	{
		if (_declared.TryGetValue(nodeId, out var cached))
			return cached;

		var tags = _store.LoadTags(nodeId);
		_declared[nodeId] = tags;
		return tags;
	}

	private Dictionary<string, string> LoadWorkspacesLocked()
	{
		if (_workspaces != null)
			return _workspaces;

		_workspaces = new Dictionary<string, string>(_store.LoadWorkspaces(), StringComparer.OrdinalIgnoreCase);
		return _workspaces;
	}

	/// <summary>Saves; on failure the cache is dropped so the next call reloads the file's real state, and the exception propagates.</summary>
	private static void Persist(Action save, Action dropCache)
	{
		try
		{
			save();
		}
		catch
		{
			dropCache();
			throw;
		}
	}

	/// <summary>FR.24.2 — Linear tags for the keys in the name whose team is known; computed, never stored.</summary>
	private List<SessionTagModel> DeriveNameTags(string? sessionName, IReadOnlyDictionary<string, string> workspaces)
	{
		var tags = new List<SessionTagModel>();
		foreach (var key in _parser.FindNameKeys(sessionName))
		{
			var team = key[..key.IndexOf('-')];
			if (!workspaces.TryGetValue(team, out var workspace))
				continue;

			var tag = _parser.BuildLinearTag(key, workspace);
			tag.Source = SessionTagSource.Name;
			tags.Add(tag);
		}

		return tags;
	}

	/// <summary>
	/// FR.24.1/FR.24.7 order: by kind; within a kind name-derived first, then declared newest first. A name-derived
	/// tag replaces a declared one with the same key.
	/// </summary>
	private IReadOnlyList<SessionTagModel> Merge(
		IReadOnlyList<SessionTagModel> declared, string? sessionName, IReadOnlyDictionary<string, string> workspaces)
	{
		var named = DeriveNameTags(sessionName, workspaces);
		var all   = named.Concat(declared.Where(d => FindByKey(named, d.Key) == null));

		return all
			.OrderBy(t => t.Kind)
			.ThenBy(t => t.Source == SessionTagSource.Name ? 0 : 1)
			.ThenByDescending(t => t.AddedUtc)
			.Select(Copy)
			.ToList();
	}

	private static SessionTagModel? FindByKey(IEnumerable<SessionTagModel> tags, string key) =>
		tags.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

	private void RaiseChanged(string? nodeId)
	{
		try
		{
			SessionTagsChanged?.Invoke(this, new SessionTagsChangedEventArgs(nodeId));
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "SessionTagService: SessionTagsChanged subscriber failed for node {NodeId}", nodeId);
		}
	}

	private static SessionTagModel Copy(SessionTagModel tag) => new()
	{
		Kind     = tag.Kind,
		Key      = tag.Key,
		Label    = tag.Label,
		Url      = tag.Url,
		Source   = tag.Source,
		AddedUtc = tag.AddedUtc,
	};
}
