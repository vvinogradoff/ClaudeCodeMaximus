using System;
using System.Collections.Generic;

namespace TessynDesktop.Services;

/// <summary>
/// Remembers, per session, whether Claude Code still has it for <c>--resume</c> (its transcript exists in a Claude CLI <c>projects</c> directory). The remote clients read it as
/// <c>SessionStateDto.Resumable</c> (the blue square of the Avalonia tree). The answer comes from one batch sweep of the whole tree, run on an interval (like the Avalonia tree's own
/// resumability check) and never from a file probe per session, because the state of every session is built for every <c>tree.snapshot</c>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionResumabilityCache : IDisposable
{
	/// <summary>
	/// Raised, with the NodeIds whose flag changed, after a sweep. The first sweep only sets the baseline and raises nothing. Raised on the sweeping thread.
	/// </summary>
	event Action<IReadOnlyList<string>>? Changed;

	/// <summary>The flag of a session; null while no sweep has run yet or the session was not in the tree at the last sweep.</summary>
	bool? TryGet(string nodeId);

	/// <summary>Sweeps every session of the tree now and returns the NodeIds whose flag changed (empty for the first sweep). Safe to call from a background thread; one sweep at a time.</summary>
	IReadOnlyList<string> Refresh();

	/// <summary>Sweeps now, then on every interval, on a background thread. A second call does nothing.</summary>
	void Start();
}
