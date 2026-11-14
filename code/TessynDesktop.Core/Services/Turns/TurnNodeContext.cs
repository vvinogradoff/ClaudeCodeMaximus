using System.Collections.Generic;
using System.Threading;

namespace TessynDesktop.Services;

/// <summary>Per-node state the engine shares between the sends of one node: the busy count, the running-flag share and the cancellation sources.</summary>
/// <remarks>Created by Claude</remarks>
internal sealed class TurnNodeContext
{
	private readonly object _gate = new();
	private readonly List<CancellationTokenSource> _active = [];
	private int _busy;
	private bool _runningOpen;

	public void Enter()
	{
		lock (_gate) _busy++;
	}

	/// <summary>Leaves a send.</summary>
	public void Leave()
	{
		lock (_gate) _busy = _busy > 0 ? _busy - 1 : 0;
	}

	/// <summary>
	/// Ends the node's busy period if no send is in it: atomically with <see cref="Enter"/> and <see cref="TryOpenRunning"/>, so a send that
	/// enters meanwhile either keeps the period (and the share) open, or finds the share closed and opens its own. False when a send is busy.
	/// </summary>
	public bool TryBecomeIdle(out bool shareWasOpen)
	{
		lock (_gate)
		{
			shareWasOpen = false;
			if (_busy > 0)
				return false;

			shareWasOpen = _runningOpen;
			_runningOpen = false;
			return true;
		}
	}

	/// <summary>The send holding the turn lock (null between turns): the target of a streaming process's events.</summary>
	public TurnRun? Active { get; set; }

	/// <summary>The observer of the latest send that had one: it renders events outside any turn and receives the idle notifications.</summary>
	public ITurnObserver? LastObserver { get; set; }

	/// <summary>The one callback bound to the node's streaming process; it routes each event to the active run, or to a background handler.</summary>
	public System.Action<Models.ClaudeStreamEvent>? StreamHandler { get; set; }

	/// <summary>True while at least one send of the node is between <see cref="Enter"/> and <see cref="Leave"/>.</summary>
	public bool IsBusy
	{
		get { lock (_gate) return _busy > 0; }
	}

	/// <summary>Takes the node's one share of the daemon's running flag; false when it already holds it.</summary>
	public bool TryOpenRunning()
	{
		lock (_gate)
		{
			if (_runningOpen)
				return false;

			_runningOpen = true;
			return true;
		}
	}

	public void Add(CancellationTokenSource cts)
	{
		lock (_gate) _active.Add(cts);
	}

	public void Remove(CancellationTokenSource cts)
	{
		lock (_gate) _active.Remove(cts);
	}

	/// <summary>
	/// Cancels every registered source, newest first; returns true when there was one. A queued send must be cancelled before the running
	/// one: ending the running send releases the turn lock, and a queued send that is not cancelled yet would take it and start a process.
	/// </summary>
	public bool CancelAll()
	{
		List<CancellationTokenSource> snapshot;
		lock (_gate) snapshot = [.. _active];

		for (var i = snapshot.Count - 1; i >= 0; i--)
		{
			try { snapshot[i].Cancel(); }
			catch (System.ObjectDisposedException) { /* the send just finished */ }
		}

		return snapshot.Count > 0;
	}
}
