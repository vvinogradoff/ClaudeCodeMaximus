using System;

namespace TessynDesktop;

public static partial class Constants
{
	/// <summary>
	/// Elevated runs requested by an agent (FR.15.16 to FR.15.20, tdcp-spec 10.A13): file names, limits, timeouts, the texts of the tools and of the outcome turn,
	/// and the fixed wrapper script (<see cref="WrapperScript"/>) the approved command runs in.
	/// </summary>
	/// <remarks>Created by Claude</remarks>
	public static class ElevatedRuns
	{
		// ── Tool names ──
		public const string RequestToolName = "request_elevated_run";
		public const string CancelToolName = "cancel_elevated_run";
		public const string ListToolName = "list_elevated_runs";

		// ── Run folder (%AppData%\TessynDesktop\elevated-runs\<id>\) ──
		public const string FolderName = "elevated-runs";
		public const string RequestFileName = "request.json";
		public const string OutputLogFileName = "output.log";
		public const string StartedFileName = "started.json";
		public const string ResultFileName = "result.json";
		public const string StopFileName = "stop";

		// ── Request limits (FR.15.16, FR.15.18) ──
		public const int MaxOpenPerSession = 3;
		public const int MaxOpenTotal = 10;
		public const int MaxCommandLength = 4000;
		public const int MaxReasonLength = 300;
		public const int DefaultTimeoutMinutes = 60;
		public const int MinTimeoutMinutes = 1;
		public const int MaxTimeoutMinutes = 240;
		public const int IdLength = 8;
		public const int IdPrefixMinLength = 4;
		public const int MaxListedRegisteredDirs = 12;

		/// <summary>The folder-name infix of a sibling worktree: <c>&lt;project&gt;-wt-&lt;suffix&gt;</c> (FR.15.16).</summary>
		public const string WorktreeInfix = "-wt-";

		// ── Timing (FR.15.18, FR.15.19) ──
		public static readonly TimeSpan PendingLifetime = TimeSpan.FromHours(24);
		public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
		public static readonly TimeSpan StartedFileTimeout = TimeSpan.FromSeconds(60);
		public static readonly TimeSpan ClientReportTimeout = TimeSpan.FromMinutes(5);
		public static readonly TimeSpan StopGracePeriod = TimeSpan.FromSeconds(30);

		// ── Launch (FR.15.19) ──
		public const string PowerShellFileName = "powershell.exe";
		public const string PowerShellArgumentsPrefix = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ";
		public const int MaxLaunchLineLength = 30_000;
		public const long OutputCapBytes = 20L * 1024 * 1024;
		public const string OutputTruncatedMarker = "[output truncated at 20 MB]";

		// ── Outcome turn (FR.15.19) ──
		public const int OutcomeTailLines = 200;
		public const int OutcomeTailLineLength = 500;
		public const int OutcomeTailMaxChars = 20_000;
		public const int OutcomeCommandLength = 500;

		// ── Focus item and notification (FR.23.31) ──
		public const string ItemKeyPrefix = "elevated:";
		public const int ItemTitleCommandLength = 120;
		public const int NotificationCommandLength = 300;
		public const int ListCommandLength = 80;
		public const int ListReasonLength = 80;
		public const int LogCommandLength = 300;

		// ── Tool descriptions ──
		public const string RequestToolDescription =
			"Ask the user to run ONE command line as administrator. Nothing runs until the user approves it in the Tessyn desktop app: they see the command exactly as you give it, your reason and the working directory on a card in their Focus view, then confirm the Windows administrator prompt. The call returns at once with a request id; the outcome (status, exit code, duration, the last lines of output, the log path) arrives later as a new turn in this session - do not wait for it and do not poll. Use it only for what really needs administrator rights, for example the WPF end-to-end tests: `powershell -NoProfile -ExecutionPolicy Bypass -File tools\\e2e\\Run-WpfE2E.ps1 --no-build --filter \"...\"` in your worktree. Give the exact command (no placeholders), a one-line reason, and a working directory that is your own, a project directory or a sibling `-wt-` worktree. Requests expire after 24 hours; withdraw one you no longer need with cancel_elevated_run.";

		public const string CancelToolDescription =
			"Withdraw your own elevated run request while it is still pending (the user has not approved it yet). No outcome turn follows. A request the user already approved cannot be withdrawn: the user can stop it from the card. The id may be a unique prefix of at least 4 characters.";

		public const string ListToolDescription =
			"List your own elevated run requests, newest first: id, status, command, reason and, when finished, the exit code and duration or the failure. status=active (default: pending, launching, running) or all. Use it to find a request after a context loss; it never shows another session's runs.";

		// ── Tool texts ──
		public const string CommandInvalidText = "The command must be one non-empty line of at most 4,000 characters.";
		public const string ReasonRequiredText = "'reason' is required and must not be blank.";
		public const string TimeoutInvalidText = "timeoutMinutes must be a whole number from 1 to 240.";
		public const string WorkingDirRequiredText = "'workingDir' is required.";
		public const string LaunchLineTooLongText = "The command is too long to be started: its launch line would exceed 30,000 characters.";

		/// <summary>Arg 0 = the folder as given (made absolute).</summary>
		public const string DirMissingFormat = "'{0}' does not exist.";

		/// <summary>Arg 0 = the folder, arg 1 = the registered projects (the first 12 and "(+N more)").</summary>
		public const string DirNotAllowedFormat = "'{0}' is not allowed for an elevated run. Use the session's working directory, a folder in a project of the Tessyn tree, or a sibling worktree named '<project>-wt-*'. Registered: {1}.";

		public const string SessionLimitText = "This session already has 3 elevated runs waiting for the user. Wait for them or withdraw one with cancel_elevated_run.";
		public const string TotalLimitText = "Too many elevated runs are waiting for the user (10). Try again later.";
		public const string DaemonElevatedText = "The daemon already runs as administrator, so this session's commands do too. Run the command directly.";

		/// <summary>Arg 0 = the id of the pending request.</summary>
		public const string AlreadyPendingFormat = "Elevated run {0} is already pending with this command and working directory - not requested again.";

		/// <summary>Arg 0 = the id.</summary>
		public const string RequestedFormat = "Elevated run {0} requested (pending). The user was notified and must approve it in the Tessyn desktop app, on the Focus card. It expires in 24 hours. The outcome will arrive as a new turn in this session - end your turn instead of waiting.";

		public const string WithdrawnFormat = "Elevated run {0} withdrawn.";
		public const string UnknownRunFormat = "Unknown elevated run '{0}'.";
		public const string IdRequiredText = "'requestId' is required.";
		public const string IdTooShortFormat = "Elevated run id '{0}' is too short: use at least 4 characters.";
		public const string IdAmbiguousFormat = "Elevated run id '{0}' matches more than one of your requests: use more characters.";
		public const string AlreadyActiveFormat = "Elevated run {0} is already {1} - the user can stop it from the card.";
		public const string AlreadyFinalFormat = "Elevated run {0} is already {1}.";
		public const string NoRunsText = "No elevated runs.";
		public const string ListInvalidStatusFormat = "Invalid status '{0}' - use active or all.";

		/// <summary>Arg 0 = id, 1 = status, 2 = command, 3 = reason.</summary>
		public const string ListLineFormat = "{0} [{1}] {2} - {3}";

		public const string ListExitSuffixFormat = " - exit {0}, {1}";
		public const string ListFailureSuffixFormat = " - {0}";

		// ── Attention item (FR.23.31) ──
		public const string ItemTitleFormat = "Run as administrator: {0}";
		public const string ItemReasonFormat = "Reason: {0}";
		public const string ItemWorkingDirFormat = "Working directory: {0}";
		public const string ItemRequestedByFormat = "Requested by: {0} ({1})";
		public const string ItemRequestedByWorkerFormat = "Requested by worker: {0} ({1})";
		public const string ItemApproveNote = "Approve this on the PC (Tessyn desktop app).";
		public const string ItemFenceCharacter = "`";
		public const int ItemMinFenceLength = 3;

		/// <summary>Arg 0 = the item id; the elevated-run item cannot be closed by the session.</summary>
		public const string ResolveRefusalFormat = "{0} is an elevated-run request; withdraw it with cancel_elevated_run.";

		/// <summary>SYSTEM entry text in place of title and body (FR.23.8); arg 0 = id, 1 = reason.</summary>
		public const string SystemEntryFormat = "Elevated run {0} requested: {1}";

		/// <summary>Resolution note; arg 0 = id, 1 = status words.</summary>
		public const string ResolutionNoteFormat = "Elevated run {0}: {1}";

		public const string ResolutionNoteExitFormat = ", exit code {0}, {1}";

		// ── Notification (FR.23.31) ──
		public const string NotificationFormat = "{0} ({1}) asks to run a command as administrator: {2}";
		public const string NotificationCommandFormat = "Command: {0}";
		public const string NotificationCutNote = "... (cut, see the card)";
		public const string NotificationDirFormat = "Working directory: {0}";
		public const string NotificationApproveLine = "Approve it on the PC.";

		// ── Status words (outcome header, resolution note) ──
		public const string WordsCompleted = "completed";
		public const string WordsDeclined = "declined by the user";
		public const string WordsDeclinedAtPrompt = "declined at the Windows prompt";
		public const string WordsFailedFormat = "failed to start: {0}";
		public const string WordsTimedOutFormat = "timed out after {0} min (the process was killed)";
		public const string WordsCancelledByUser = "cancelled by the user (stopped while running)";
		public const string WordsCancelledByAgent = "cancelled by the agent (withdrawn)";
		public const string WordsExpired = "expired (not approved within 24 hours)";

		/// <summary>The plain status names for <c>list_elevated_runs</c> and the "already ..." texts, in <see cref="TessynDesktop.Models.ElevatedRunStatus"/> order.</summary>
		public static readonly string[] StatusNames =
		[
			"pending", "launching", "running", "completed", "declined", "declined at the Windows prompt", "failed to start", "timed out", "cancelled", "expired",
		];

		// ── Failure texts (FR.15.18, FR.15.19) ──
		public const string FailureNotStartedText = "The elevated process did not start within 60 seconds.";
		public const string FailureClientSilentText = "The client did not report the start.";
		public const string FailureNoResultText = "The elevated process ended without a result.";
		public const string FailureDaemonRestartedText = "The daemon restarted before the outcome was known.";
		public const string StopNotConfirmedText = "Stop was requested; the process may still be running.";

		// ── Outcome prompt (FR.15.19) ──
		public const string OutcomeHeaderFormat = "[Elevated run {0} {1}] {2}";
		public const string OutcomeCommandFormat = "Command: {0}";
		public const string OutcomeWorkingDirFormat = "Working directory: {0}";
		public const string OutcomeNoteFormat = "Note: {0}";
		public const string OutcomeExitCodeFormat = "Exit code: {0}";
		public const string OutcomeDurationFormat = "Duration: {0}";
		public const string OutcomeLogFormat = "Log: {0}";
		public const string OutcomeOutputHeaderFormat = "Output (last {0} lines):";
		public const string OutcomeCutSuffix = "...";

		/// <summary>The note of the delivery, and its name in a merged note ("Worker results (2): ..."); arg 0 = the run id.</summary>
		public const string MailboxNoteFormat = "Elevated run {0}";

		// ── Service errors of the TDCP methods ──
		public const string UnknownRequestText = "Unknown elevated run request.";
		public const string NotRunningFormat = "This request is not running ({0}).";
		public const string NotLaunchingFormat = "This request is not launching ({0}).";

		// ── Wrapper (FR.15.19) ──
		public const string RunnerPlaceholderCommand = "COMMAND";
		public const string RunnerPlaceholderDir = "DIR";
		public const string RunnerPlaceholderRun = "RUN";
		public const string RunnerPlaceholderSeconds = "SECONDS";
		public const string RunnerPlaceholderCap = "CAP";
		public const string RunnerPlaceholderMarker = "MARKER";

		/// <summary>The values <c>started.json</c> and <c>result.json</c> carry as <c>ended</c> (tdcp-spec 10.A13).</summary>
		public const string EndedExited = "exited";
		public const string EndedTimedOut = "timedOut";
		public const string EndedStopped = "stopped";

		/// <summary>
		/// The fixed wrapper (FR.15.19), one script for every run. The request's command, working directory, run folder, time limit in seconds and output cap are
		/// substituted for the <c>@@NAME@@</c> tokens in one pass by <see cref="TessynDesktop.Services.ElevatedRunLaunchBuilder"/>; the strings go in as
		/// PowerShell single-quoted here-strings, so no character of the command needs escaping and nothing is read from a file. The script starts
		/// <c>cmd.exe /d /s /c "&lt;command&gt;"</c>, appends its standard output and error to <c>output.log</c> (capped), writes <c>started.json</c>, polls once a second
		/// for the <c>stop</c> file and the time limit (killing the tree with <c>taskkill /T /F</c>), and writes <c>result.json</c> last, atomically.
		/// </summary>
		public const string WrapperScript = """
			$ErrorActionPreference = 'Stop'
			$cmd = @'
			@@COMMAND@@
			'@
			$dir = @'
			@@DIR@@
			'@
			$run = @'
			@@RUN@@
			'@
			$limit = @@SECONDS@@
			$cap = @@CAP@@
			$marker = @'
			@@MARKER@@
			'@
			Add-Type -TypeDefinition @'
			using System;
			using System.Diagnostics;
			using System.IO;
			using System.Text;
			using System.Threading;
			public sealed class ErRun
			{
				private readonly FileStream _log;
				private readonly long _cap;
				private readonly string _marker;
				private readonly object _gate = new object();
				private readonly UTF8Encoding _utf8 = new UTF8Encoding(false);
				private Thread _outThread;
				private Thread _errThread;
				private long _written;
				private bool _truncated;
				public Process Proc;
				public ErRun(string logPath, long cap, string marker)
				{
					_log = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
					_cap = cap;
					_marker = marker;
				}
				public void Start(string file, string args, string dir)
				{
					ProcessStartInfo psi = new ProcessStartInfo(file, args);
					psi.WorkingDirectory = dir;
					psi.UseShellExecute = false;
					psi.CreateNoWindow = true;
					psi.RedirectStandardInput = true;
					psi.RedirectStandardOutput = true;
					psi.RedirectStandardError = true;
					Encoding oem = Encoding.Default;
					try { oem = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage); } catch (Exception) { }
					psi.StandardOutputEncoding = oem;
					psi.StandardErrorEncoding = oem;
					Proc = Process.Start(psi);
					Proc.StandardInput.Close();
					_outThread = Pump(Proc.StandardOutput);
					_errThread = Pump(Proc.StandardError);
				}
				private Thread Pump(StreamReader reader)
				{
					Thread t = new Thread(delegate ()
					{
						char[] buf = new char[4096];
						int n;
						while ((n = reader.Read(buf, 0, buf.Length)) > 0)
							Append(new string(buf, 0, n));
					});
					t.IsBackground = true;
					t.Start();
					return t;
				}
				private void Append(string text)
				{
					byte[] bytes = _utf8.GetBytes(text);
					lock (_gate)
					{
						if (_truncated)
							return;
						long room = _cap - _written;
						if (bytes.Length <= room)
						{
							_log.Write(bytes, 0, bytes.Length);
							_written += bytes.Length;
						}
						else
						{
							if (room > 0)
								_log.Write(bytes, 0, (int)room);
							byte[] note = _utf8.GetBytes(Environment.NewLine + _marker + Environment.NewLine);
							_log.Write(note, 0, note.Length);
							_truncated = true;
						}
						_log.Flush();
					}
				}
				public void Finish(int milliseconds)
				{
					if (_outThread != null) _outThread.Join(milliseconds);
					if (_errThread != null) _errThread.Join(milliseconds);
					lock (_gate)
					{
						_log.Flush();
						_log.Dispose();
					}
				}
			}
			'@
			function Write-Json($name, $value) {
				$path = Join-Path $run $name
				$tmp = $path + '.tmp'
				[IO.File]::WriteAllText($tmp, ($value | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding($false)))
				Move-Item -LiteralPath $tmp -Destination $path -Force
			}
			$logPath = Join-Path $run 'output.log'
			$stopPath = Join-Path $run 'stop'
			$comspec = $env:ComSpec
			if (-not $comspec) { $comspec = 'cmd.exe' }
			$er = New-Object ErRun($logPath, $cap, $marker)
			try {
				$er.Start($comspec, ('/d /s /c "' + $cmd + '"'), $dir)
			} catch {
				[IO.File]::AppendAllText($logPath, ('[could not start: ' + $_.Exception.Message + ']' + [Environment]::NewLine))
				exit 1
			}
			Write-Json 'started.json' ([ordered]@{ pid = $PID; commandPid = $er.Proc.Id; startedUtc = [DateTime]::UtcNow.ToString('o') })
			$ended = 'exited'
			$clock = [Diagnostics.Stopwatch]::StartNew()
			while (-not $er.Proc.WaitForExit(1000)) {
				if (Test-Path -LiteralPath $stopPath) { $ended = 'stopped'; break }
				if ($clock.Elapsed.TotalSeconds -ge $limit) { $ended = 'timedOut'; break }
			}
			if ($ended -ne 'exited') {
				& taskkill.exe /T /F /PID $er.Proc.Id | Out-Null
				[void]$er.Proc.WaitForExit(10000)
			}
			$code = -1
			if ($er.Proc.HasExited) { $code = $er.Proc.ExitCode }
			$er.Finish(5000)
			Write-Json 'result.json' ([ordered]@{ exitCode = $code; endedUtc = [DateTime]::UtcNow.ToString('o'); ended = $ended })
			""";
	}
}
