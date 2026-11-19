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

	/// <summary>No daemon runs but the setting "Start the background daemon when none is running" is off: the client waits for Tessyn (the Avalonia app) to come up.</summary>
	Disabled,
}

/// <summary>Starts the Avalonia daemon host when no daemon answers (A23 Q6).</summary>
/// <remarks>Created by Claude</remarks>
public interface IDaemonHostLauncher
{
	/// <summary>Starts the background host when <paramref name="status"/> says there is no daemon. At most one host is ever started per cool-down, and never beside a running one.</summary>
	Task<HostLaunchOutcome> EnsureStartedAsync(DaemonDiscoveryStatus status, CancellationToken cancellationToken);

	/// <summary>
	/// Starts the host now, <paramref name="mode"/>'s way, whatever the start setting says (FR.32.11): a restart has ended the daemon. Never beside a host that
	/// holds the guard. An elevated start that is declined or fails starts the host normally and raises <see cref="ElevationRefused"/> (FR.32.13).
	/// </summary>
	Task<HostLaunchOutcome> StartNowAsync(HostStartMode mode, CancellationToken cancellationToken);

	/// <summary>An elevated start was declined or failed, and the host was started normally instead (FR.32.13). Raised on the thread that started the host.</summary>
	event Action<ElevationRefusal>? ElevationRefused;
}

/// <summary>Never starts anything (a custom environment, and the default of code that does not need a host).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class NoDaemonHostLauncher : IDaemonHostLauncher
{
	public event Action<ElevationRefusal>? ElevationRefused
	{
		add { }
		remove { }
	}

	public Task<HostLaunchOutcome> EnsureStartedAsync(DaemonDiscoveryStatus status, CancellationToken cancellationToken) => Task.FromResult(HostLaunchOutcome.NotNeeded);

	public Task<HostLaunchOutcome> StartNowAsync(HostStartMode mode, CancellationToken cancellationToken) => Task.FromResult(HostLaunchOutcome.NotNeeded);
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
	/// <summary>
	/// Starts <paramref name="executable"/>, elevated through the administrator prompt for <see cref="HostStartMode.Elevated"/>; null when it did not start.
	/// A declined prompt throws a <see cref="System.ComponentModel.Win32Exception"/> with <see cref="Constants.Switch.ElevationCancelledError"/>.
	/// </summary>
	IHostProcess? Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, HostStartMode mode);
}

/// <summary>Starts the real process, without a console window; the host outlives this client.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProcessHostStarter : IHostProcessStarter
{
	public IHostProcess? Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, HostStartMode mode)
	{
		var process = Process.Start(CreateStartInfo(executable, arguments, workingDirectory, mode));
		return process == null ? null : new ProcessHandle(process);
	}

	/// <summary>
	/// How the host is started. Normal: a direct start without a console window. Elevated (FR.32.10): a shell start with the <c>runas</c> verb, which shows
	/// the administrator prompt; a shell start has no pipes (the host needs none) and takes the arguments as one command line.
	/// </summary>
	public ProcessStartInfo CreateStartInfo(string executable, IReadOnlyList<string> arguments, string workingDirectory, HostStartMode mode)
	{
		if (mode == HostStartMode.Elevated)
		{
			return new ProcessStartInfo
			{
				FileName = executable,
				Arguments = string.Join(' ', arguments.Select(QuoteArgument)),
				WorkingDirectory = workingDirectory,
				UseShellExecute = true,
				Verb = Constants.Switch.RunAsVerb,
				WindowStyle = ProcessWindowStyle.Hidden,
				ErrorDialog = false,
			};
		}

		var info = new ProcessStartInfo
		{
			FileName = executable,
			WorkingDirectory = workingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
		};
		foreach (var argument in arguments)
			info.ArgumentList.Add(argument);
		return info;
	}

	private string QuoteArgument(string argument) =>
		argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"') ? argument : "\"" + argument.Replace("\"", "\\\"") + "\"";

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
				catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
				{
					return true;   // gone, or an elevated process this one may not query: the guard and the cool-down still prevent a second host
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
	private readonly Func<bool> _isEnabled;
	private readonly Func<bool> _isElevated;
	private IHostProcess? _started;
	private bool _starting;
	private bool _elevationRefused;
	private DateTimeOffset _lastAttempt = DateTimeOffset.MinValue;

	/// <param name="isEnabled">The setting "Start the background daemon when none is running", read at every call (a change takes effect at once); null = always on.</param>
	/// <param name="isElevated">The setting "Start daemon elevated" (FR.32.10), read at every start; null = always off.</param>
	public DaemonHostLauncher(string runFolder, string? sourceLocation, IHostProcessStarter starter, IHostPresence presence, TimeProvider time, TimeSpan cooldown, ILogger logger, Func<string, string, BuildCopyResult>? copier = null, Func<bool>? isEnabled = null, Func<bool>? isElevated = null)
	{
		_isElevated = isElevated ?? (() => false);
		_isEnabled = isEnabled ?? (() => true);
		_copier = copier ?? BuildCopier.Copy;
		_runFolder = runFolder.TrimEnd('\\', '/');
		_sourceLocation = sourceLocation;
		_starter = starter;
		_presence = presence;
		_time = time;
		_cooldown = cooldown;
		_logger = logger;
	}

	public event Action<ElevationRefusal>? ElevationRefused;

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

		// Side-by-side: with the setting off this client never starts a host, whatever the state of the guard. It waits for Tessyn (the Avalonia app) to come up.
		if (!_isEnabled())
			return HostLaunchOutcome.Disabled;

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

		return await CopyAndStartAsync(null, cancellationToken);
	}

	public async Task<HostLaunchOutcome> StartNowAsync(HostStartMode mode, CancellationToken cancellationToken)
	{
		lock (_gate)
		{
			if (_starting)
				return HostLaunchOutcome.AlreadyStarting;

			_started?.Dispose();
			_started = null;

			if (_presence.IsHostRunning())
				return HostLaunchOutcome.HostPresent;   // the ended daemon's guard is still held, or another host came up: never a second one

			_lastAttempt = _time.GetUtcNow();
			_starting = true;
		}

		return await CopyAndStartAsync(mode, cancellationToken);
	}

	/// <summary>
	/// The way an automatic start goes (FR.32.10): elevated when the setting is on, unless an elevated start was declined or failed earlier in this run, so
	/// a declined prompt never comes back by itself (FR.32.13). Only <see cref="StartNowAsync"/> asks again.
	/// </summary>
	private HostStartMode AutomaticMode()
	{
		lock (_gate)
			return _isElevated() && !_elevationRefused ? HostStartMode.Elevated : HostStartMode.Normal;
	}

	/// <summary>Refreshes the host folder and starts the host (<paramref name="mode"/> null: the automatic way); the caller has set <c>_starting</c>, which this clears.</summary>
	private async Task<HostLaunchOutcome> CopyAndStartAsync(HostStartMode? mode, CancellationToken cancellationToken)
	{
		try
		{
			var chosen = mode ?? AutomaticMode();
			// The copy is blocking disk I/O over several hundred files: never on the caller's (possibly the UI) thread, and never under the lock.
			await Task.Run(UpdateHostFolder, cancellationToken);
			return StartHost(chosen);
		}
		finally
		{
			lock (_gate)
				_starting = false;
		}
	}

	private HostLaunchOutcome StartHost(HostStartMode mode)
	{
		if (!File.Exists(HostExecutable))
		{
			_logger.LogWarning("No daemon host at {Path}. Build the solution and start the client through run-wpf.ps1 once, or start TessynDesktop yourself", HostExecutable);
			return HostLaunchOutcome.HostMissing;
		}

		if (mode == HostStartMode.Elevated)
		{
			var refusal = TryStartElevated();
			if (refusal == null)
				return HostLaunchOutcome.Started;

			// FR.32.13: a declined or failed administrator prompt starts the host normally, so Tessyn stays usable, and says so.
			lock (_gate)
				_elevationRefused = true;
			ElevationRefused?.Invoke(refusal);
		}

		IHostProcess? started;
		try
		{
			started = _starter.Start(HostExecutable, [Constants.Switch.BackgroundArgument], HostFolder, HostStartMode.Normal);
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

	/// <summary>Starts the host through the administrator prompt; null when it started, otherwise why it did not.</summary>
	private ElevationRefusal? TryStartElevated()
	{
		IHostProcess? started;
		try
		{
			started = _starter.Start(HostExecutable, [Constants.Switch.BackgroundArgument], HostFolder, HostStartMode.Elevated);
		}
		catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == Constants.Switch.ElevationCancelledError)
		{
			_logger.LogInformation("The administrator prompt for the daemon host was declined; starting it normally");
			return new ElevationRefusal(Declined: true, Reason: null);
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
		{
			_logger.LogWarning(ex, "Could not start the daemon host {Path} elevated; starting it normally", HostExecutable);
			return new ElevationRefusal(Declined: false, Reason: ex.Message);
		}

		lock (_gate)
		{
			// A shell start may succeed without handing back a process handle: the host was started all the same, so no normal host is started beside it
			// (it could take the guard first). Discovery finds the started host; the cool-down and the guard cover the rest.
			_started = started;
			_elevationRefused = false;   // approved: a later automatic start may ask again
		}

		_logger.LogInformation("Started the background daemon host {Path} elevated", HostExecutable);
		return null;
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
