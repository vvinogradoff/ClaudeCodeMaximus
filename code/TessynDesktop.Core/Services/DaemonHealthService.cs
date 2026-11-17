using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <inheritdoc cref="IDaemonHealthService" />
/// <remarks>Created by Claude</remarks>
public sealed class DaemonHealthService : IDaemonHealthService
{
	private const double BytesPerMegabyte = 1024d * 1024d;

	private readonly IDaemonRunInfo _runInfo;
	private readonly IEventLog? _eventLog;
	private readonly IRemoteTreeReader _tree;
	private readonly ISessionRegistry _registry;
	private readonly ISessionIndexActivity? _indexActivity;
	private readonly TimeProvider _time;
	private readonly DateTimeOffset _startedUtc;
	private readonly string _logDirectory;

	/// <param name="startedUtc">When the daemon started; default = the start of this process.</param>
	/// <param name="logDirectory">Where the log files are; default = the app's log folder.</param>
	public DaemonHealthService(
		IDaemonRunInfo runInfo, IRemoteTreeReader tree, ISessionRegistry registry,
		IEventLog? eventLog = null, ISessionIndexActivity? indexActivity = null,
		TimeProvider? time = null, DateTimeOffset? startedUtc = null, string? logDirectory = null)
	{
		_runInfo       = runInfo;
		_tree          = tree;
		_registry      = registry;
		_eventLog      = eventLog;
		_indexActivity = indexActivity;
		_time          = time ?? TimeProvider.System;
		_startedUtc    = startedUtc ?? new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime());
		_logDirectory  = logDirectory ?? Path.Combine(Constants.DataRoot, Constants.Diagnostics.LogsFolderName);
	}

	/// <inheritdoc />
	public DaemonHealthResult Get()
	{
		var scans = _indexActivity?.ActiveScans ?? 0;
		var index = new DaemonIndexHealthDto(
			scans > 0 ? RemoteProtocol.Health.IndexStates.Scanning : RemoteProtocol.Health.IndexStates.Idle,
			_indexActivity?.LastScanFinishedUtc,
			Math.Max(0, scans - 1));

		return new DaemonHealthResult(
			Math.Max(0, (long)(_time.GetUtcNow() - _startedUtc).TotalSeconds),
			_runInfo.RunId,
			_eventLog?.HeadSeq ?? 0,
			_tree.Snapshot().AllSessions.Count(s => _registry.IsSessionRunning(s.NodeId)),
			index,
			ReadLogBytes(),
			Math.Round(Environment.WorkingSet / BytesPerMegabyte, 1),
			GC.CollectionCount(2));
	}

	private long? ReadLogBytes()
	{
		try
		{
			return Directory.Exists(_logDirectory)
				? Directory.EnumerateFiles(_logDirectory, Constants.DaemonHealth.LogFilePattern).Sum(f => new FileInfo(f).Length)
				: null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}
}
