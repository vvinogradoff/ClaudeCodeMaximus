using System;
using System.IO;
using System.Text.Json;
using Serilog;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Publishes <c>daemon.json</c> next to the app data so a client process can find the endpoint without reading appsettings.json.
/// Written at startup (atomically) and deleted on a clean exit; a file left behind by a crash names a pid that no longer runs,
/// which clients detect by checking the pid.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonDiscoveryFile
{
	private static readonly ILogger _log = Log.ForContext<DaemonDiscoveryFile>();

	private readonly IDaemonRunInfo _runInfo;
	private readonly ITdcpTokenStore _tokens;

	public DaemonDiscoveryFile(IDaemonRunInfo runInfo, ITdcpTokenStore tokens) : this(runInfo, tokens, GetDefaultPath())
	{
	}

	/// <summary>Constructor for testing — allows injecting a custom file path.</summary>
	public DaemonDiscoveryFile(IDaemonRunInfo runInfo, ITdcpTokenStore tokens, string path)
	{
		_runInfo = runInfo;
		_tokens  = tokens;
		FilePath = path;
	}

	/// <summary>Full path of the file.</summary>
	public string FilePath { get; }

	private static string GetDefaultPath()
	{
		return Path.Combine(Constants.DataRoot, Constants.Remote.Discovery.FileName);
	}

	/// <summary>Writes the file for the listener on <paramref name="port"/>. Never throws: discovery is a convenience, not a startup requirement.</summary>
	public void Write(int port)
	{
		try
		{
			var model = new DaemonDiscoveryModel(
				$"http://127.0.0.1:{port}{Constants.Agent.TdcpEndpointPath}",
				_runInfo.ProcessId,
				Constants.Remote.ProtocolVersion,
				_runInfo.RunId,
				_tokens.FilePath);

			var dir = Path.GetDirectoryName(FilePath);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);

			var tempPath = FilePath + ".tmp";
			File.WriteAllText(tempPath, JsonSerializer.Serialize(model, TdcpJsonOptions.Default));
			File.Move(tempPath, FilePath, overwrite: true);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "DaemonDiscoveryFile: could not write {Path}", FilePath);
		}
	}

	/// <summary>Deletes the file on a clean exit, but only when it still describes this run (a newer instance may have replaced it).</summary>
	public void Clear()
	{
		try
		{
			if (!File.Exists(FilePath))
				return;

			var current = JsonSerializer.Deserialize<DaemonDiscoveryModel>(File.ReadAllText(FilePath), TdcpJsonOptions.Default);
			if (current?.RunId == _runInfo.RunId)
				File.Delete(FilePath);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "DaemonDiscoveryFile: could not remove {Path}", FilePath);
		}
	}
}
