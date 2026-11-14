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

	/// <summary>Leaves a send; returns true when it was the last one.</summary>
	public bool Leave()
	{
		lock (_gate)
		{
			_busy = _busy > 0 ? _busy - 1 : 0;
			return _busy == 0;
		}
	}

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

	/// <summary>Gives the share back; false when it was not held.</summary>
	public bool TryCloseRunning()
	{
		lock (_gate)
		{
			if (!_runningOpen)
				return false;

			_runningOpen = false;
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

	/// <summary>Cancels every registered source; returns true when there was one.</summary>
	public bool CancelAll()
	{
		List<CancellationTokenSource> snapshot;
		lock (_gate) snapshot = [.. _active];

		foreach (var cts in snapshot)
		{
			try { cts.Cancel(); }
			catch (System.ObjectDisposedException) { /* the send just finished */ }
		}

		return snapshot.Count > 0;
	}
}
