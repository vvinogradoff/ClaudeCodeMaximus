using System;
using System.Threading;

namespace TessynDesktop.Services;

/// <summary>The store's state for one session file: its lock, the current snapshot and the bookkeeping of its sidecar.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class FileIndexState
{
	/// <summary>Serializes index updates of this file (readers use the published snapshot and do not take it unless it is stale).</summary>
	public object Gate { get; } = new();

	private EntryIndexSnapshot? _snapshot;

	/// <summary>The published index; written under <see cref="Gate"/>, read by anyone.</summary>
	public EntryIndexSnapshot? Snapshot
	{
		get => Volatile.Read(ref _snapshot);
		set => Volatile.Write(ref _snapshot, value);
	}

	/// <summary>Set by a rewrite notification: the next read rebuilds with a new file id.</summary>
	public bool ForceRebuild { get; set; }

	/// <summary>True while the in-memory index is ahead of its sidecar.</summary>
	public bool Dirty { get; set; }

	public DateTime LastPersistUtc { get; set; }

	public long LastUsed { get; set; }
}
