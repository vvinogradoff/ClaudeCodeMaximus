using System.IO;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>The wire form of an elevated run (tdcp-spec 10.A13).</summary>
/// <remarks>Created by Claude</remarks>
public static class ElevatedRunDtoMapping
{
	/// <summary>
	/// The run as the wire shows it. The command is always whole. For the relay (<paramref name="relay"/>) the working directory and the log path are left out
	/// and the last folder name is sent instead: no path leaves the machine (tdcp-spec 8.2).
	/// </summary>
	public static ElevatedRunDto ToDto(this ElevatedRunModel run, string logPath, bool relay, string requesterShortId) => new()
	{
		Id             = run.Id,
		Status         = run.Status,
		Requester      = new ElevatedRequesterDto(run.RequesterNodeId, run.RequesterName, requesterShortId),
		TopLevel       = run.TopLevelNodeId == null ? null : new ElevatedTopLevelDto(run.TopLevelNodeId, run.TopLevelName ?? string.Empty),
		Reason         = run.Reason,
		WorkingDir     = relay ? null : run.WorkingDir,
		WorkingDirName = relay ? FolderName(run.WorkingDir) : null,
		Command        = run.Command,
		TimeoutMinutes = run.TimeoutMinutes,
		CreatedUtc     = run.CreatedUtc,
		ExpiresUtc     = run.ExpiresUtc,
		StartedUtc     = run.StartedUtc,
		EndedUtc       = run.EndedUtc,
		ExitCode       = run.ExitCode,
		CancelledBy    = run.CancelledBy,
		Note           = run.Note,
		Failure        = run.Failure,
		LogPath        = relay ? null : logPath,
	};

	/// <summary>The launch description of an approved run for the wire.</summary>
	public static ElevatedLaunchDto ToDto(this ElevatedRunLaunchModel launch) => new(
		launch.RequestId, launch.FileName, launch.Arguments, launch.WorkingDirectory, launch.RunDir, launch.Verb, launch.StartTimeoutSeconds);

	/// <summary>The last folder name of <paramref name="path"/> (the whole path for a drive root).</summary>
	public static string FolderName(string path)
	{
		var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
		return string.IsNullOrEmpty(name) ? path : name;
	}
}
