using System;

namespace TessynDesktop.Services;

/// <summary>What the session index is doing now: for <c>search.reindex</c> (is a scan already running?) and <c>daemon.health</c>.</summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionIndexActivity
{
	/// <summary>Scans running or waiting now (each upkeep run, whoever started it).</summary>
	int ActiveScans { get; }

	/// <summary>When the last scan ended (successfully or not); null before the first one in this run.</summary>
	DateTimeOffset? LastScanFinishedUtc { get; }
}
