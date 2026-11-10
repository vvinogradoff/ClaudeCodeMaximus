using System;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Decides when a memory spike deserves a heap capture: at or above the threshold, then not again until memory has dropped
/// below the threshold or risen another <see cref="Constants.Diagnostics.SpikeRearmRiseMb"/>, and at most once per hour.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class MemorySpikeGate
{
	private readonly DiagnosticsSettingsModel _settings;
	private readonly TimeProvider _time;
	private bool _armed = true;
	private double _lastCaptureMb;
	private long? _lastCaptureTimestamp;

	public MemorySpikeGate(DiagnosticsSettingsModel settings, TimeProvider? time = null)
	{
		_settings = settings;
		_time = time ?? TimeProvider.System;
	}

	/// <summary>True when a capture should start now; the gate then disarms itself.</summary>
	public bool ShouldCapture(double privateMb)
	{
		if (!_armed && (privateMb < _settings.MemorySpikeThresholdMb || privateMb >= _lastCaptureMb + Constants.Diagnostics.SpikeRearmRiseMb))
			_armed = true;

		if (!_armed || privateMb < _settings.MemorySpikeThresholdMb)
			return false;

		if (_lastCaptureTimestamp is { } last
			&& _time.GetElapsedTime(last).TotalMinutes < Constants.Diagnostics.SpikeMinIntervalMinutes)
			return false;

		_armed = false;
		_lastCaptureMb = privateMb;
		_lastCaptureTimestamp = _time.GetTimestamp();
		return true;
	}
}
