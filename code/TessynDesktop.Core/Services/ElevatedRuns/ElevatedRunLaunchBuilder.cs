using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Builds the launch description of an approved run (FR.15.19): <c>powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand &lt;base64&gt;</c>,
/// where the encoded script is the fixed <see cref="Constants.ElevatedRuns.WrapperScript"/> with the request's command, working directory, run folder, time limit
/// and output cap embedded as data. Nothing the elevated process executes is read from a file a normal process could change.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class ElevatedRunLaunchBuilder
{
	[GeneratedRegex("@@(COMMAND|DIR|RUN|SECONDS|CAP|MARKER)@@")]
	private static partial Regex TokenPattern();

	/// <summary>
	/// The launch description for <paramref name="run"/> with its files in <paramref name="runDir"/>. The line is built from the given record only.
	/// Throws <see cref="McpToolException"/> when the encoded argument line would exceed <see cref="Constants.ElevatedRuns.MaxLaunchLineLength"/>.
	/// </summary>
	public ElevatedRunLaunchModel Build(ElevatedRunModel run, string runDir)
	{
		var script    = BuildScript(run.Command, run.WorkingDir, runDir, checked(run.TimeoutMinutes * 60));
		var encoded   = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
		var arguments = Constants.ElevatedRuns.PowerShellArgumentsPrefix + encoded;

		if (Constants.ElevatedRuns.PowerShellFileName.Length + 1 + arguments.Length > Constants.ElevatedRuns.MaxLaunchLineLength)
			throw new McpToolException(Constants.ElevatedRuns.LaunchLineTooLongText);

		return new ElevatedRunLaunchModel(
			run.Id,
			Constants.ElevatedRuns.PowerShellFileName,
			arguments,
			run.WorkingDir,
			runDir,
			TessynDesktop.Models.Remote.RemoteProtocol.ElevatedRuns.RunAsVerb,
			(int)Constants.ElevatedRuns.StartedFileTimeout.TotalSeconds);
	}

	/// <summary>The wrapper script with the data embedded; one pass, so a value that looks like a token is never substituted again.</summary>
	public string BuildScript(string command, string workingDir, string runDir, int seconds)
	{
		return TokenPattern().Replace(Constants.ElevatedRuns.WrapperScript, match => match.Groups[1].Value switch
		{
			Constants.ElevatedRuns.RunnerPlaceholderCommand => command,
			Constants.ElevatedRuns.RunnerPlaceholderDir     => workingDir,
			Constants.ElevatedRuns.RunnerPlaceholderRun     => runDir,
			Constants.ElevatedRuns.RunnerPlaceholderSeconds => seconds.ToString(CultureInfo.InvariantCulture),
			Constants.ElevatedRuns.RunnerPlaceholderCap     => Constants.ElevatedRuns.OutputCapBytes.ToString(CultureInfo.InvariantCulture),
			_                                               => Constants.ElevatedRuns.OutputTruncatedMarker,
		});
	}
}
