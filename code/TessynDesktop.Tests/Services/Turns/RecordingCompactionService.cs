using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TessynDesktop.Models;
using TessynDesktop.Services;

namespace TessynDesktop.Tests.Services.Turns;

/// <summary>
/// Compaction service that records how it was called (which variant, with which options, the turn lock count at that moment, in the
/// shared transcript log) and answers with a scripted result.
/// </summary>
/// <remarks>Created by Claude</remarks>
internal sealed class RecordingCompactionService : ISessionCompactionService
{
	private readonly Func<int> _lockCount;
	private readonly List<string> _log;

	public RecordingCompactionService(Func<int> lockCount, List<string> log)
	{
		_lockCount = lockCount;
		_log       = log;
	}

	/// <summary>One entry per call: the variant, the options and the lock count at the call.</summary>
	public List<(string Variant, SessionCompactionOptionsModel Options, int LockCount)> Calls { get; } = [];

	public SessionCompactionResultModel Result { get; set; } =
		new(SessionCompactionOutcome.Compacted, null, "[compacted]", SessionDetached: true);

	public Task<SessionCompactionResultModel> CompactAsync(SessionNodeModel node, SessionCompactionOptionsModel options, CancellationToken cancellationToken = default) =>
		Record("compact", options);

	public Task<SessionCompactionResultModel> CompactWhileHoldingTurnLockAsync(SessionNodeModel node, SessionCompactionOptionsModel options, CancellationToken cancellationToken = default) =>
		Record("compactHeld", options);

	private Task<SessionCompactionResultModel> Record(string variant, SessionCompactionOptionsModel options)
	{
		Calls.Add((variant, options, _lockCount()));
		_log.Add(variant);
		return Task.FromResult(Result);
	}
}
