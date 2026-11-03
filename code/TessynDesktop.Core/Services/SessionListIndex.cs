using System;
using System.Collections.Generic;
using System.Linq;
using TessynDesktop.Models.Agent;

namespace TessynDesktop.Services;

/// <summary>
/// Lookup structures over a session snapshot list for the <c>list_sessions</c> page builder
/// (FR.15.13): by NodeId, direct workers, descendants, directory totals and Short Node Ids.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionListIndex
{
	private readonly Dictionary<string, SessionSnapshotModel> _byId = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, List<SessionSnapshotModel>> _workersOf = new(StringComparer.OrdinalIgnoreCase);
	private Dictionary<string, string>? _shortIds;
	private Dictionary<string, SessionListSectionModel>? _directories;

	public SessionListIndex(IReadOnlyList<SessionSnapshotModel> sessions)
	{
		foreach (var s in sessions)
			_byId.TryAdd(s.NodeId, s);

		foreach (var s in _byId.Values)
		{
			if (string.IsNullOrEmpty(s.SupervisorNodeId)
				|| string.Equals(s.SupervisorNodeId, s.NodeId, StringComparison.OrdinalIgnoreCase)
				|| !_byId.ContainsKey(s.SupervisorNodeId))
				continue;

			if (!_workersOf.TryGetValue(s.SupervisorNodeId, out var list))
				_workersOf[s.SupervisorNodeId] = list = [];
			list.Add(s);
		}
	}

	public IEnumerable<SessionSnapshotModel> All => _byId.Values;

	public SessionSnapshotModel? Find(string? nodeId)
	{
		if (string.IsNullOrEmpty(nodeId))
			return null;

		return _byId.GetValueOrDefault(nodeId);
	}

	/// <summary>The listed supervisor of <paramref name="s"/>; null when it has none, points at itself or is no longer in the tree.</summary>
	public SessionSnapshotModel? FindSupervisor(SessionSnapshotModel s)
	{
		if (string.IsNullOrEmpty(s.SupervisorNodeId)
			|| string.Equals(s.SupervisorNodeId, s.NodeId, StringComparison.OrdinalIgnoreCase))
			return null;

		return Find(s.SupervisorNodeId);
	}

	/// <summary>A Worker Session is one with a supervisor or a non-zero orchestration depth, even if the supervisor has left the tree.</summary>
	public bool IsWorker(SessionSnapshotModel s) =>
		!string.IsNullOrEmpty(s.SupervisorNodeId) || s.OrchestrationDepth > 0;

	public IReadOnlyList<SessionSnapshotModel> DirectWorkers(string nodeId) =>
		_workersOf.TryGetValue(nodeId, out var list) ? list : [];

	public IReadOnlyList<SessionSnapshotModel> Descendants(string rootNodeId)
	{
		var result  = new List<SessionSnapshotModel>();
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootNodeId };
		var queue   = new Queue<string>();
		queue.Enqueue(rootNodeId);

		while (queue.Count > 0)
		{
			foreach (var worker in DirectWorkers(queue.Dequeue()))
			{
				if (!visited.Add(worker.NodeId))
					continue;

				result.Add(worker);
				queue.Enqueue(worker.NodeId);
			}
		}

		return result;
	}

	/// <summary>True when following <see cref="SessionSnapshotModel.SupervisorNodeId"/> links from <paramref name="s"/> leads back to it.</summary>
	public bool IsInSupervisorCycle(SessionSnapshotModel s)
	{
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var current = s;

		while (!string.IsNullOrEmpty(current.SupervisorNodeId))
		{
			if (string.Equals(current.SupervisorNodeId, s.NodeId, StringComparison.OrdinalIgnoreCase))
				return true;

			if (!visited.Add(current.SupervisorNodeId) || !_byId.TryGetValue(current.SupervisorNodeId, out var next))
				return false;

			current = next;
		}

		return false;
	}

	/// <summary>Shortest prefix of the NodeId, at least <see cref="Constants.Agent.ShortNodeIdMinLength"/> characters, that no other NodeId shares.</summary>
	public string GetShortId(string nodeId)
	{
		_shortIds ??= BuildShortIds();
		return _shortIds.TryGetValue(nodeId, out var shortId) ? shortId : nodeId;
	}

	/// <summary>The Directory section for <paramref name="directoryPath"/> with whole-directory counts, before any filter.</summary>
	public SessionListSectionModel? GetDirectorySection(string directoryPath)
	{
		_directories ??= BuildDirectories();
		return _directories.GetValueOrDefault(directoryPath);
	}

	private Dictionary<string, string> BuildShortIds()
	{
		var ids    = _byId.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		for (var i = 0; i < ids.Count; i++)
		{
			var shared = 0;
			if (i > 0)
				shared = Math.Max(shared, CommonPrefixLength(ids[i - 1], ids[i]));
			if (i < ids.Count - 1)
				shared = Math.Max(shared, CommonPrefixLength(ids[i], ids[i + 1]));

			var length = Math.Min(ids[i].Length, Math.Max(Constants.Agent.ShortNodeIdMinLength, shared + 1));
			result[ids[i]] = ids[i][..length];
		}

		return result;
	}

	private Dictionary<string, SessionListSectionModel> BuildDirectories()
	{
		var result = new Dictionary<string, SessionListSectionModel>(StringComparer.OrdinalIgnoreCase);

		foreach (var group in _byId.Values.GroupBy(s => s.DirectoryPath, StringComparer.OrdinalIgnoreCase))
		{
			var workers = group.Count(IsWorker);
			result[group.Key] = new SessionListSectionModel
			{
				Kind           = SessionListSectionKind.Directory,
				DirectoryLabel = group.First().DirectoryLabel,
				DirectoryPath  = group.First().DirectoryPath,
				TopLevelCount  = group.Count() - workers,
				WorkerCount    = workers,
			};
		}

		return result;
	}

	private int CommonPrefixLength(string a, string b)
	{
		var max = Math.Min(a.Length, b.Length);
		var i   = 0;
		while (i < max && char.ToLowerInvariant(a[i]) == char.ToLowerInvariant(b[i]))
			i++;
		return i;
	}
}
