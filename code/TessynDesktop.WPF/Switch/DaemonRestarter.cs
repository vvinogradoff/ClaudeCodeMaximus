using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TessynDesktop.Models.Remote;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Switch;

/// <summary>A running daemon that is older than the newest host build.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DaemonStaleInfo(DateTime DaemonBuildUtc, DateTime NewestBuildUtc);

/// <summary>What the "Restart daemon" button did.</summary>
/// <remarks>Created by Claude</remarks>
public enum DaemonRestartOutcome
{
	/// <summary>The user said no; nothing changed.</summary>
	Cancelled,

	/// <summary>The daemon cannot be asked to exit from here: it is an older host or a visible Avalonia window. Exit it by hand.</summary>
	NotSupported,

	/// <summary>The daemon was asked to exit but is still running after the wait.</summary>
	DidNotExit,

	/// <summary>The daemon ended; the client restarts itself, and the new client starts the host from the newest build and connects.</summary>
	ClientRestarting,
}

/// <summary>The folder a daemon process runs from and the build time of what is in it.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record RunningDaemonBuild(string Folder, DateTime BuildUtc);

/// <summary>Where the daemon process runs from and when that build was made (a host is never refreshed while it runs, so the folder is what runs).</summary>
/// <remarks>Created by Claude</remarks>
public interface IDaemonBuildProbe
{
	RunningDaemonBuild? Inspect(int pid);
}

/// <summary>Reads the build time from the folder of the process's main module.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProcessDaemonBuildProbe : IDaemonBuildProbe
{
	/// <summary>The folder with the newest write time over all its <c>TessynDesktop*.dll</c>; null when it holds none.</summary>
	public static RunningDaemonBuild? InspectFolder(string folder)
	{
		var newest = DaemonRestarter.NewestAssemblyWrite(folder);
		return newest == null ? null : new RunningDaemonBuild(folder, newest.Value);
	}

	public RunningDaemonBuild? Inspect(int pid)
	{
		try
		{
			using var process = Process.GetProcessById(pid);
			var exe = process.MainModule?.FileName;
			var folder = exe == null ? null : Path.GetDirectoryName(exe);
			return folder == null ? null : InspectFolder(folder);
		}
		catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
		{
			return null;
		}
	}
}

/// <summary>Asks the background host to exit (the named event); false when no host listens.</summary>
/// <remarks>Created by Claude</remarks>
public interface IHostExitSignal
{
	bool Signal();
}

/// <summary>Sets the host's named exit event.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class NamedEventHostExitSignal : IHostExitSignal
{
	private readonly string _name;

	public NamedEventHostExitSignal(string? name = null)
	{
		_name = name ?? RemoteProtocol.Discovery.HostExitEventName;
	}

	public bool Signal()
	{
		try
		{
			using var existing = EventWaitHandle.OpenExisting(_name);
			return existing.Set();
		}
		catch (WaitHandleCannotBeOpenedException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}
}

/// <summary>Waits for a process to end.</summary>
/// <remarks>Created by Claude</remarks>
public interface IProcessExitWaiter
{
	/// <summary>True when the process is gone (or never existed) within <paramref name="timeout"/>.</summary>
	Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Waits on the real process.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProcessExitWaiter : IProcessExitWaiter
{
	public async Task<bool> WaitForExitAsync(int pid, TimeSpan timeout, CancellationToken cancellationToken)
	{
		try
		{
			using var process = Process.GetProcessById(pid);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			linked.CancelAfter(timeout);
			try
			{
				await process.WaitForExitAsync(linked.Token);
				return true;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				return process.HasExited;
			}
		}
		catch (ArgumentException)
		{
			return true;
		}
		catch (InvalidOperationException)
		{
			return true;
		}
	}
}

/// <summary>Restarts this client after the daemon was restarted underneath it.</summary>
/// <remarks>Created by Claude</remarks>
public interface IClientRestarter
{
	void RestartClient();
}

/// <summary>
/// "The daemon is older than the build": the check at connect, and the one-click restart (A23 Q7 follow-up). The restart asks the user, asks the background host to
/// exit (it terminates the active sessions and shuts down), waits for it to end, and restarts this client, whose start finds no daemon, starts the host from the
/// newest build (the launcher refreshes the host folder) and connects.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonRestarter
{
	/// <summary>Build times closer than this are the same build (file systems round).</summary>
	public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

	private readonly string? _sourceLocation;
	private readonly IDaemonBuildProbe _probe;
	private readonly IDialogService _dialogs;
	private readonly IHostExitSignal _signal;
	private readonly IProcessExitWaiter _waiter;
	private readonly IClientRestarter _restarter;
	private readonly TimeSpan _exitTimeout;
	private readonly ILogger _logger;
	private readonly string? _hostFolder;
	private readonly IDaemonChangeDetector? _changeDetector;

	public DaemonRestarter(string? sourceLocation, IDaemonBuildProbe probe, IDialogService dialogs, IHostExitSignal signal, IProcessExitWaiter waiter, IClientRestarter restarter, TimeSpan exitTimeout, ILogger logger, string? hostFolder = null, IDaemonChangeDetector? changeDetector = null)
	{
		_changeDetector = changeDetector;
		_hostFolder = hostFolder;
		_sourceLocation = sourceLocation;
		_probe = probe;
		_dialogs = dialogs;
		_signal = signal;
		_waiter = waiter;
		_restarter = restarter;
		_exitTimeout = exitTimeout;
		_logger = logger;
	}

	/// <summary>
	/// The stale info when the daemon process <paramref name="pid"/> runs a build older than the newest one under the source location; otherwise null (also when either is unknown).
	/// With a change detector (FR.32.9) the daemon's source decides: the banner only when it changed between the two commits; the time rule (FR.32.7) decides when that cannot be told.
	/// </summary>
	public DaemonStaleInfo? Check(int pid)
	{
		if (string.IsNullOrWhiteSpace(_sourceLocation))
			return null;

		var running = _probe.Inspect(pid);
		var buildDir = SourceBuilds.FindNewestBuild(_sourceLocation, Constants.Switch.HostProjectFolder, Constants.Switch.HostAssemblyName);
		if (running == null || buildDir == null)
			return null;

		// A daemon that is not the host of this run folder (a visible Avalonia window from [Publish], the rollback) cannot be restarted from here: no banner.
		if (_hostFolder != null && !string.Equals(Path.GetFullPath(running.Folder).TrimEnd('\\'), Path.GetFullPath(_hostFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
			return null;

		var newest = NewestAssemblyWrite(buildDir);
		if (newest == null)
			return null;

		var change = _changeDetector?.Evaluate(running.Folder, buildDir, _sourceLocation);
		if (change != null)
			LogDecision(change);

		if (change?.Decision == DaemonChangeDecision.Unchanged)
			return null;

		if (change?.Decision == DaemonChangeDecision.Changed)
			return new DaemonStaleInfo(running.BuildUtc, newest.Value);

		return newest.Value - running.BuildUtc > Tolerance ? new DaemonStaleInfo(running.BuildUtc, newest.Value) : null;
	}

	private void LogDecision(DaemonChangeResult change)
	{
		var outcome = change.Decision switch
		{
			DaemonChangeDecision.Changed => "banner",
			DaemonChangeDecision.Unchanged => "no banner",
			_ => "the build time decides",
		};
		_logger.LogInformation("Stale daemon check: running commit {RunningCommit}, newest build commit {NewestCommit}: {Decision} ({Reason}); {Outcome}", change.RunningCommit ?? "unknown", change.NewestCommit ?? "unknown", change.Decision, change.Reason, outcome);
	}

	/// <summary>
	/// The newest write time over every <c>TessynDesktop*.dll</c> in the folder (the daemon code lives in Core and Protocol as well as in the exe assembly, and a rebuild may leave
	/// <c>TessynDesktop.dll</c> alone); null when there is none.
	/// </summary>
	public static DateTime? NewestAssemblyWrite(string folder)
	{
		DateTime? newest = null;
		foreach (var file in Directory.EnumerateFiles(folder, Constants.Switch.HostAssemblyPattern, SearchOption.TopDirectoryOnly))
		{
			var time = File.GetLastWriteTimeUtc(file);
			if (newest == null || time > newest)
				newest = time;
		}

		return newest;
	}

	/// <summary>The question of the restart dialog.</summary>
	public static string ConfirmText(int activeSessions) => activeSessions > 0
		? $"Restart the Tessyn daemon on the newest build? {activeSessions} active session(s) will be terminated, and this window restarts."
		: "Restart the Tessyn daemon on the newest build? This window restarts.";

	public async Task<DaemonRestartOutcome> RestartAsync(int pid, int activeSessions, CancellationToken cancellationToken)
	{
		if (!await _dialogs.ConfirmAsync(new ConfirmDialogRequest(Constants.Switch.RestartTitle, ConfirmText(activeSessions), Constants.Switch.RestartConfirm, Constants.DialogTexts.Cancel, IsDestructive: activeSessions > 0)))
			return DaemonRestartOutcome.Cancelled;

		if (!_signal.Signal())
		{
			_logger.LogWarning("The daemon (pid {Pid}) does not listen for an exit request: an older host or a visible Avalonia window", pid);
			return DaemonRestartOutcome.NotSupported;
		}

		if (!await _waiter.WaitForExitAsync(pid, _exitTimeout, cancellationToken))
		{
			_logger.LogWarning("The daemon (pid {Pid}) did not exit within {Timeout}", pid, _exitTimeout);
			return DaemonRestartOutcome.DidNotExit;
		}

		_logger.LogInformation("The daemon exited; restarting the client");
		_restarter.RestartClient();
		return DaemonRestartOutcome.ClientRestarting;
	}
}

/// <summary>Restarts the client with the script that waits for this process to exit, then shuts this process down.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ScriptClientRestarter : IClientRestarter
{
	private readonly IUpdateScriptLauncher _launcher;
	private readonly string _scriptDirectory;
	private readonly string _exePath;
	private readonly IReadOnlyList<string> _arguments;
	private readonly int _processId;
	private readonly Action _shutdown;

	public ScriptClientRestarter(IUpdateScriptLauncher launcher, string scriptDirectory, string exePath, IReadOnlyList<string> arguments, int processId, Action shutdown)
	{
		_launcher = launcher;
		_scriptDirectory = scriptDirectory;
		_exePath = exePath;
		_arguments = arguments;
		_processId = processId;
		_shutdown = shutdown;
	}

	public void RestartClient()
	{
		var path = Path.Combine(_scriptDirectory, string.Format(Constants.Switch.RestartScriptFileFormat, _processId));
		File.WriteAllText(path, WpfSelfUpdater.BuildRestartScript(_exePath, _arguments, _processId, Path.GetDirectoryName(_exePath) ?? _scriptDirectory), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
		_launcher.Start(path);
		_shutdown();
	}
}
