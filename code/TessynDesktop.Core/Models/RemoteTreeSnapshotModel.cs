using System;
using System.Collections.Generic;
using System.Linq;

namespace TessynDesktop.Models;

/// <summary>A consistent copy of the whole tree, taken in one pass under the settings lock.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class RemoteTreeSnapshotModel
{
	private readonly Dictionary<string, RemoteSessionRecord> _byId;

	/// <param name="directories">The tree in model order.</param>
	/// <param name="allSessions">Every session, Worker Sessions included, in tree order: per container its groups first, then its sessions (the desktop's display order).</param>
	public RemoteTreeSnapshotModel(IReadOnlyList<RemoteDirectoryRecord> directories, IReadOnlyList<RemoteSessionRecord> allSessions)
	{
		Directories = directories;
		AllSessions = allSessions;
		_byId       = new Dictionary<string, RemoteSessionRecord>(allSessions.Count, StringComparer.Ordinal);
		foreach (var s in allSessions)
			_byId.TryAdd(s.NodeId, s);
	}

	/// <summary>The tree in model order.</summary>
	public IReadOnlyList<RemoteDirectoryRecord> Directories { get; }

	/// <summary>Every session in tree order (see the constructor).</summary>
	public IReadOnlyList<RemoteSessionRecord> AllSessions { get; }

	/// <summary>
	/// True when <paramref name="session"/> is a Worker Session whose supervisor is in the tree; an orphan worker
	/// (supervisor missing) counts as top-level, as on the desktop.
	/// </summary>
	public bool IsWorker(RemoteSessionRecord session) =>
		!string.IsNullOrEmpty(session.SupervisorNodeId)
		&& session.SupervisorNodeId != session.NodeId
		&& _byId.ContainsKey(session.SupervisorNodeId);

	/// <summary>The session with <paramref name="nodeId"/>, or null.</summary>
	public RemoteSessionRecord? Find(string nodeId) => _byId.GetValueOrDefault(nodeId);

	/// <summary>
	/// Follows SupervisorNodeId up to the top-level session <paramref name="session"/> belongs to (itself when it is no worker or its
	/// supervisor is missing). A supervisor cycle ends at the first repeated session.
	/// </summary>
	public RemoteSessionRecord ResolveTopLevel(RemoteSessionRecord session)
	{
		var current = session;
		var visited = new HashSet<string>(StringComparer.Ordinal) { session.NodeId };
		while (!string.IsNullOrEmpty(current.SupervisorNodeId)
			&& _byId.TryGetValue(current.SupervisorNodeId, out var next)
			&& visited.Add(next.NodeId))
		{
			current = next;
		}
		return current;
	}

	/// <summary>The top-level sessions in tree order.</summary>
	public IReadOnlyList<RemoteSessionRecord> TopLevelSessions() => AllSessions.Where(s => !IsWorker(s)).ToList();

	/// <summary>Number of Worker Sessions directly under each supervisor (only supervisors with at least one appear).</summary>
	public IReadOnlyDictionary<string, int> CountWorkers()
	{
		var counts = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var session in AllSessions.Where(IsWorker))
			counts[session.SupervisorNodeId!] = counts.GetValueOrDefault(session.SupervisorNodeId!) + 1;
		return counts;
	}
}
