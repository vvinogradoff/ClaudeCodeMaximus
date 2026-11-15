using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Switch;

/// <summary>What the launcher did for one discovery result.</summary>
/// <remarks>Created by Claude</remarks>
public enum HostLaunchOutcome
{
	/// <summary>Nothing to start: a daemon answers (or its file names a live process), or starting is switched off.</summary>
	NotNeeded,

	/// <summary>The background host was started.</summary>
	Started,

	/// <summary>A host started earlier by this client is still starting or the cool-down has not passed; nothing new was started.</summary>
	AlreadyStarting,

	/// <summary>Another process already holds the daemon host guard (a host is starting, or a visible Avalonia window runs); nothing was started.</summary>
	HostPresent,

	/// <summary>No host executable exists in the run folder's <c>host</c> folder.</summary>
	HostMissing,

	/// <summary>The process could not be started.</summary>
	Failed,
}

/// <summary>Starts the Avalonia daemon host when no daemon answers (A23 Q6).</summary>
/// <remarks>Created by Claude</remarks>
public interface IDaemonHostLauncher
{
	/// <summary>Starts the background host when <paramref name="status"/> says there is no daemon. At most one host is ever started per cool-down, and never beside a running one.</summary>
	Task<HostLaunchOutcome> EnsureStartedAsync(DaemonDiscoveryStatus status, CancellationToken cancellationToken);
}

/// <summary>Never starts anything (a custom environment, and the default of code that does not need a host).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class NoDaemonHostLauncher : IDaemonHostLauncher
{
	public Task<HostLaunchOutcome> EnsureStartedAsync(DaemonDiscoveryStatus status, CancellationToken cancellationToken) => Task.FromResult(HostLaunchOutcome.NotNeeded);
}

/// <summary>A started host process.</summary>
/// <remarks>Created by Claude</remarks>
public interface IHostProcess : IDisposable
{
	bool HasExited { get; }
}

/// <summary>Starts a process (a seam for tests).</summary>
/// <remarks>Created by Claude</remarks>
public interface IHostProcessStarter
{
	/// <summary>Starts <paramref name="executable"/>; null when it did not start.</summary>
	IHostProcess? Start(string executable, IReadOnlyList<string> arguments, string workingDirectory);
}

/// <summary>Starts the real process, without a console window; the host outlives this client.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProcessHostStarter : IHostProcessStarter
{
	public IHostProcess? Start(string executable, IReadOnlyList<string> arguments, string workingDirectory)
	{
		var info = new ProcessStartInfo
		{
			FileName = executable,
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		foreach (var argument in arguments)
			info.ArgumentList.Add(argument);

		var process = Process.Start(info);
		return process == null ? null : new ProcessHandle(process);
	}

	private sealed class ProcessHandle(Process process) : IHostProcess
	{
		public bool HasExited
		{
			get
			{
				try
				{
					return process.HasExited;
				}
				catch (InvalidOperationException)
				{
					return true;
				}
			}
		}

		public void Dispose() => process.Dispose();
	}
}

/// <summary>Whether a daemon host holds the guard (a seam for tests; the real one looks at the named mutex).</summary>
/// <remarks>Created by Claude</remarks>
public interface IHostPresence
{
	bool IsHostRunning();
}

/// <summary>Looks at the session-local mutex the Avalonia host holds for its whole life.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class MutexHostPresence : IHostPresence
{
	private readonly string _name;

	public MutexHostPresence(string? name = null)
	{
		_name = name ?? RemoteProtocol.Discovery.HostMutexName;
	}

	public bool IsHostRunning()
	{
		try
		{
			using var existing = Mutex.OpenExisting(_name);
			return true;
		}
		catch (WaitHandleCannotBeOpenedException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return true;
		}
	}
}

/// <summary>
/// <see cref="IDaemonHostLauncher"/> for the real run folder: <c>&lt;run folder&gt;\host\TessynDesktop.exe --background</c>, brought up to date from the newest Avalonia build first.
/// Safe to call repeatedly (the connection loop does): a host this client started is not started again while it lives and for a cool-down after it, and the host itself exits at
/// once when another one holds the guard, so two hosts never run.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonHostLauncher : IDaemonHostLauncher
{
	private readonly string _runFolder;
	private readonly string? _sourceLocation;
	private readonly IHostProcessStarter _starter;
	private readonly IHostPresence _presence;
	private readonly TimeProvider _time;
	private readonly TimeSpan _cooldown;
	private readonly ILogger _logger;
	private readonly object _gate = new();

	private readonly Func<string, string, BuildCopyResult> _copier;
	private IHostProcess? _started;
	private bool _starting;
	private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

	public DaemonHostLauncher(string runFolder, string? sourceLocation, IHostProcessStarter starter, IHostPresence presence, TimeProvider time, TimeSpan cooldown, ILogger logger, Func<string, string, BuildCopyResult>? copier = null)
	{
		_copier = copier ?? BuildCopier.Copy;
		_runFolder = runFolder.TrimEnd('\\', '/');
		_sourceLocation = sourceLocation;
		_starter = starter;
		_presence = presence;
		_time = time;
		_cooldown = cooldown;
		_logger = logger;
	}

	/// <summary>The folder the host runs from.</summary>
	public string HostFolder => Path.Combine(_runFolder, Constants.Switch.HostFolderName);

	/// <summary>The host executable.</summary>
	public string HostExecutable => Path.Combine(HostFolder, Constants.Switch.HostExeName);

	/// <summary>True for the statuses that mean "no daemon is running".</summary>
	public static bool NeedsHost(DaemonDiscoveryStatus status) =>
		status is DaemonDiscoveryStatus.NoDaemonFile or DaemonDiscoveryStatus.StaleFile or DaemonDiscoveryStatus.InvalidFile;

	public async Task<HostLaunchOutcome> EnsureStartedAsync(DaemonDiscoveryStatus status, CancellationToken cancellationToken)
	{
		if (!NeedsHost(status))
			return HostLaunchOutcome.NotNeeded;

		lock (_gate)
		{
			if (_starting || _started is { HasExited: false })
				return HostLaunchOutcome.AlreadyStarting;

			var now = _time.GetUtcNow();
			if (now - _lastAttempt < _cooldown)
				return HostLaunchOutcome.AlreadyStarting;

			_started?.Dispose();
			_started = null;

			if (_presence.IsHostRunning())
			{
				// A host holds the guard but has not published daemon.json yet (it is starting), or a visible Avalonia window is the daemon. Wait for it.
				return HostLaunchOutcome.HostPresent;
			}

			_lastAttempt = now;
			_starting = true;
		}

		try
		{
			// The copy is blocking disk I/O over several hundred files: never on the caller's (possibly the UI) thread, and never under the lock.
			await Task.Run(UpdateHostFolder, cancellationToken);
			return StartHost();
		}
		finally
		{
			lock (_gate)
				_starting = false;
		}
	}

	/// <summary>Forgets the last attempt so the next call may start a host at once (the user asked for a restart).</summary>
	public void ResetCooldown()
	{
		lock (_gate)
			_lastAttempt = DateTimeOffset.MinValue;
	}

	private HostLaunchOutcome StartHost()
	{
		if (!File.Exists(HostExecutable))
		{
			_logger.LogWarning("No daemon host at {Path}. Build the solution and start the client through run-wpf.ps1 once, or start TessynDesktop yourself", HostExecutable);
			return HostLaunchOutcome.HostMissing;
		}

		IHostProcess? started;
		try
		{
			started = _starter.Start(HostExecutable, [Constants.Switch.BackgroundArgument], HostFolder);
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
		{
			_logger.LogError(ex, "Could not start the daemon host {Path}", HostExecutable);
			return HostLaunchOutcome.Failed;
		}

		if (started == null)
			return HostLaunchOutcome.Failed;

		lock (_gate)
			_started = started;

		_logger.LogInformation("Started the background daemon host {Path}", HostExecutable);
		return HostLaunchOutcome.Started;
	}

	/// <summary>Copies the newest Avalonia build into the host folder (nothing is running there: the guard was free).</summary>
	private void UpdateHostFolder()
	{
		if (string.IsNullOrWhiteSpace(_sourceLocation))
			return;

		try
		{
			var buildDir = SourceBuilds.FindNewestBuild(_sourceLocation, Constants.Switch.HostProjectFolder, Constants.Switch.HostAssemblyName);
			if (buildDir == null)
				return;

			var result = _copier(buildDir, HostFolder);
			_logger.LogInformation("Host folder updated from {Build}: {Copied} copied, {Skipped} skipped, {UpToDate} current", buildDir, result.Copied, result.Skipped, result.UpToDate);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.LogWarning(ex, "Could not update the host folder {Folder}", HostFolder);
		}
	}
}
