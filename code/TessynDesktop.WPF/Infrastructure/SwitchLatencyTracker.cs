using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Infrastructure;

/// <summary>
/// Measures a session switch from the click to the first paint reported by the page (a JS message) and logs it at
/// Information, so the §6 budgets (cold 400/600 ms, warm 150 ms) can be checked from the log.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SwitchLatencyTracker
{
	private readonly ILogger<SwitchLatencyTracker> _logger;
	private readonly TimeProvider _time;

	private string? _nodeId;
	private long _startTimestamp;
	private bool _warm;

	public SwitchLatencyTracker(ILogger<SwitchLatencyTracker> logger) : this(logger, TimeProvider.System)
	{
	}

	public SwitchLatencyTracker(ILogger<SwitchLatencyTracker> logger, TimeProvider time)
	{
		_logger = logger;
		_time = time;
	}

	/// <summary>The last completed switch, for tests and diagnostics.</summary>
	public SwitchLatencyRecord? Last { get; private set; }

	/// <summary>Starts timing a switch to <paramref name="nodeId"/>; a newer switch replaces an unfinished one.</summary>
	public void Begin(string nodeId)
	{
		_nodeId = nodeId;
		_warm = false;
		_startTimestamp = _time.GetTimestamp();
	}

	/// <summary>Records whether the switch was served from the in-memory cache.</summary>
	public void MarkWarm(string nodeId, bool warm)
	{
		if (_nodeId == nodeId)
			_warm = warm;
	}

	/// <summary>The page painted <paramref name="nodeId"/>; ignored unless it is the switch being timed.</summary>
	public void Painted(string nodeId, double jsMilliseconds)
	{
		if (_nodeId != nodeId)
			return;

		var elapsed = _time.GetElapsedTime(_startTimestamp);
		_nodeId = null;
		Last = new SwitchLatencyRecord(nodeId, elapsed.TotalMilliseconds, jsMilliseconds, _warm);
		_logger.LogInformation("session-switch {NodeId} {Kind} click-to-first-paint {Ms:0} ms (page script {JsMs:0.0} ms)", nodeId, _warm ? "warm" : "cold", elapsed.TotalMilliseconds, jsMilliseconds);
	}
}
