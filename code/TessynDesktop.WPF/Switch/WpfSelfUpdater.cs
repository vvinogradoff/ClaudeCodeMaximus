using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Switch;

/// <summary>What the start-up update check decided.</summary>
/// <remarks>Created by Claude</remarks>
public enum SelfUpdateOutcome
{
	/// <summary>Not looked at: a test or custom environment, or the restarted process.</summary>
	Disabled,

	/// <summary>This process runs from a build output folder; it never updates itself there (rule A15).</summary>
	RunningFromBuildOutput,

	/// <summary>No source location is configured, or it holds no WPF build.</summary>
	NoBuild,

	/// <summary>The run folder already has the newest build.</summary>
	UpToDate,

	/// <summary>A newer build exists: a script was started that waits for this process to exit, copies the build and starts the client again. The caller must exit now.</summary>
	RestartScheduled,

	/// <summary>A newer build exists, but the script could not be started; the client carries on with the old build.</summary>
	ScriptFailed,
}

/// <summary>Starts the updater script (a seam for tests).</summary>
/// <remarks>Created by Claude</remarks>
public interface IUpdateScriptLauncher
{
	void Start(string scriptPath);
}

/// <summary>Runs the script with Windows PowerShell, hidden.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class PowerShellScriptLauncher : IUpdateScriptLauncher
{
	public void Start(string scriptPath)
	{
		Process.Start(new ProcessStartInfo
		{
			FileName = "powershell.exe",
			Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
			UseShellExecute = true,
		})?.Dispose();
	}
}

/// <summary>
/// The WPF self-update (decision A23 Q7, work package W13). The client runs from its own folder (<c>[Publish-WPF]</c>), never from the build output (rule A15), so a build never
/// meets a locked file. At start it compares its assembly with the newest build under <c>SourceCodesLocation</c>; when that is newer it starts a hidden script that waits for this
/// process to exit, copies the build over the run folder (the <c>host</c> sub folder is not touched) with retries, and starts the client again with <c>--no-self-update</c>, so a copy
/// that keeps failing cannot restart it forever. The copy keeps write times, so the restarted client finds itself up to date.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class WpfSelfUpdater
{
	private readonly string _runFolder;
	private readonly string? _sourceLocation;
	private readonly IUpdateScriptLauncher _launcher;
	private readonly string _scriptDirectory;
	private readonly ILogger _logger;

	public WpfSelfUpdater(string runFolder, string? sourceLocation, IUpdateScriptLauncher launcher, string scriptDirectory, ILogger logger)
	{
		_runFolder = runFolder.TrimEnd('\\', '/');
		_sourceLocation = sourceLocation;
		_launcher = launcher;
		_scriptDirectory = scriptDirectory;
		_logger = logger;
	}

	/// <summary>Checks for a newer build and, when there is one, schedules the copy and restart. Never throws.</summary>
	/// <param name="processId">This process; the script waits for it to exit.</param>
	/// <param name="restartArguments">The arguments to start the client with again (<c>--no-self-update</c> is added).</param>
	public SelfUpdateOutcome CheckAndScheduleRestart(int processId, IReadOnlyList<string> restartArguments)
	{
		try
		{
			ReportPreviousUpdate();
			if (SourceBuilds.IsBuildOutput(_runFolder))
			{
				_logger.LogWarning("Self-update: running from a build output ({Folder}); it is disabled. Start the client from {RunFolder}", _runFolder, Constants.Switch.RunFolderName);
				return SelfUpdateOutcome.RunningFromBuildOutput;
			}

			if (string.IsNullOrWhiteSpace(_sourceLocation))
			{
				_logger.LogInformation("Self-update: no source location configured (SourceCodesLocation in appsettings.json); skipped");
				return SelfUpdateOutcome.NoBuild;
			}

			var buildDir = SourceBuilds.FindNewestBuild(_sourceLocation, Constants.Switch.WpfProjectFolder, Constants.Switch.WpfAssemblyName);
			if (buildDir == null)
			{
				_logger.LogInformation("Self-update: no WPF build found under {Source}", _sourceLocation);
				return SelfUpdateOutcome.NoBuild;
			}

			if (!NeedsUpdate(buildDir, _runFolder))
			{
				_logger.LogInformation("Self-update: the run folder is current");
				return SelfUpdateOutcome.UpToDate;
			}

			_logger.LogInformation("Self-update: a newer build exists in {Build}; restarting after the copy", buildDir);
			var arguments = new List<string> { Constants.Switch.NoSelfUpdateArgument };
			arguments.AddRange(restartArguments.Where(a => !string.Equals(a, Constants.Switch.NoSelfUpdateArgument, StringComparison.OrdinalIgnoreCase)));
			var scriptPath = Path.Combine(_scriptDirectory, string.Format(Constants.Switch.UpdateScriptFileFormat, processId));
			File.WriteAllText(scriptPath, BuildScript(buildDir, _runFolder, Path.Combine(_runFolder, Constants.Switch.WpfExeName), arguments, processId, ResultPath), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			_launcher.Start(scriptPath);
			return SelfUpdateOutcome.RestartScheduled;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Self-update failed; carrying on with the current build");
			return SelfUpdateOutcome.ScriptFailed;
		}
	}

	/// <summary>Where the script records how the copy ended ("ok" or "failed ..."); the next start reads and deletes it.</summary>
	public string ResultPath => Path.Combine(_scriptDirectory, Constants.Switch.UpdateResultFileName);

	/// <summary>
	/// True when the build folder holds something the run folder does not: a file the run folder lacks, or a newest write time (over all files, not only the main assembly) that is later.
	/// The run folder's own <c>host</c> sub folder is ignored. A copy that stopped half way is thereby noticed at the next start.
	/// </summary>
	public static bool NeedsUpdate(string buildDir, string runFolder)
	{
		var buildNewest = DateTime.MinValue;
		foreach (var file in Directory.EnumerateFiles(buildDir, "*", SearchOption.AllDirectories))
		{
			buildNewest = Max(buildNewest, File.GetLastWriteTimeUtc(file));
			if (!File.Exists(Path.Combine(runFolder, Path.GetRelativePath(buildDir, file))))
				return true;
		}

		var runNewest = DateTime.MinValue;
		var hostFolder = Path.Combine(runFolder, Constants.Switch.HostFolderName) + Path.DirectorySeparatorChar;
		foreach (var file in Directory.EnumerateFiles(runFolder, "*", SearchOption.AllDirectories))
		{
			if (!file.StartsWith(hostFolder, StringComparison.OrdinalIgnoreCase))
				runNewest = Max(runNewest, File.GetLastWriteTimeUtc(file));
		}

		return buildNewest > runNewest;
	}

	private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

	/// <summary>Logs a warning when the previous update's script reported a failed copy (a mixed-version run folder), then forgets it.</summary>
	private void ReportPreviousUpdate()
	{
		try
		{
			if (!File.Exists(ResultPath))
				return;

			var text = File.ReadAllText(ResultPath).Trim();
			File.Delete(ResultPath);
			if (text.StartsWith("failed", StringComparison.OrdinalIgnoreCase))
				_logger.LogWarning("The previous self-update did not finish copying ({Result}); the run folder may hold files of two builds. It will be tried again at the next start", text);
		}
		catch (IOException)
		{
		}
	}

	/// <summary>
	/// The script text: wait for the process, copy with retries (robocopy, nothing deleted), write the result, start the client again. The client is started with
	/// <c>Process.Start</c>: <c>Start-Process</c> takes its path as a wildcard pattern, and <c>[Publish-WPF]</c> is an invalid one.
	/// </summary>
	public static string BuildScript(string sourceDir, string destinationDir, string exePath, IReadOnlyList<string> arguments, int processId, string resultPath, IReadOnlyList<int>? retryDelays = null)
	{
		var delays = string.Join(", ", retryDelays ?? Constants.Switch.ScriptRetryDelaysSeconds);
		return
$@"$source = {Quote(sourceDir)}
$dest = {Quote(destinationDir)}
$result = {Quote(resultPath)}
$delays = @({delays})
try {{ Wait-Process -Id {processId} -Timeout {Constants.Switch.ScriptWaitForExitSeconds} -ErrorAction SilentlyContinue }} catch {{ }}
$code = 16
foreach ($delay in $delays) {{
    robocopy $source $dest /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    $code = $LASTEXITCODE
    if ($code -lt 8) {{ break }}
    Start-Sleep -Seconds $delay
}}
try {{
    if ($code -lt 8) {{ Set-Content -LiteralPath $result -Value 'ok' }} else {{ Set-Content -LiteralPath $result -Value ('failed: robocopy exit code ' + $code) }}
}} catch {{ }}
{StartClientSnippet(exePath, arguments, "$dest")}";
	}

	/// <summary>A script that only waits for this process to exit and starts the client again (the daemon was restarted underneath it).</summary>
	public static string BuildRestartScript(string exePath, IReadOnlyList<string> arguments, int processId, string workingDirectory) =>
$@"try {{ Wait-Process -Id {processId} -Timeout {Constants.Switch.ScriptWaitForExitSeconds} -ErrorAction SilentlyContinue }} catch {{ }}
{StartClientSnippet(exePath, arguments, Quote(workingDirectory))}";

	private static string StartClientSnippet(string exePath, IReadOnlyList<string> arguments, string workingDirectoryExpression) =>
$@"$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = {Quote(exePath)}
$psi.Arguments = {Quote(string.Join(' ', arguments.Select(WindowsArgument)))}
$psi.WorkingDirectory = {workingDirectoryExpression}
$psi.UseShellExecute = $false
[System.Diagnostics.Process]::Start($psi) | Out-Null
";

	private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

	private static string WindowsArgument(string argument) =>
		argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0 ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";
}
