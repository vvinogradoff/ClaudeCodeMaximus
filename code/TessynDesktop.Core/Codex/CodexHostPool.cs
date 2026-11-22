using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace TessynDesktop.Codex;

/// <summary>
/// One <see cref="CodexAppServerHost"/> per Codex profile home (FR.33.6), created lazily on the first need of that profile and shared by all its
/// sessions. The host itself starts its process on the first call and stops it when idle.
/// <list type="bullet">
/// <item>Codex Default is <see cref="Constants.Codex.Profiles.DefaultProfileId"/> with no <c>CODEX_HOME</c>; any other profile is passed in with
/// its home folder (tests today, the stored profiles of phase 3).</item>
/// <item>The executable is found once, on the first host (<see cref="CodexExecutableLocator"/>, FR.33.17), and its version is logged with a
/// Warning when it is not the tested one (FR.33.6). It is never blocking.</item>
/// <item>"Terminate all" (FR.15.5) kills every host; <see cref="StopHostAsync"/> is for a deleted profile.</item>
/// </list>
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class CodexHostPool : IAsyncDisposable
{
	private static readonly ILogger _log = Log.ForContext<CodexHostPool>();

	private readonly ICodexProcessLauncher _launcher;
	private readonly CodexExecutableLocator _locator;
	private readonly TimeProvider _time;
	private readonly string _clientVersion;
	private readonly object _gate = new();
	private readonly Dictionary<string, CodexAppServerHost> _hosts = new(StringComparer.OrdinalIgnoreCase);
	private readonly string? _fixedPath;
	private readonly TessynDesktop.Services.IAppSettingsService? _appSettings;
	private string? _executablePath;
	private string? _locatedFor;
	private string? _versionCheckedFor;

	/// <param name="launcher">Starts the app-server processes; the real <see cref="CodexProcessLauncher"/> when null.</param>
	/// <param name="locator">Finds <c>codex.exe</c>; the real locator when null.</param>
	/// <param name="time">The clock of the idle shutdown and the request timeouts.</param>
	/// <param name="executablePath">A fixed executable (tests): no discovery and no version check.</param>
	/// <param name="appSettings">Supplies <c>CodexPath</c> (FR.33.17); a changed path applies to hosts created after the change.</param>
	public CodexHostPool(ICodexProcessLauncher? launcher = null, CodexExecutableLocator? locator = null, TimeProvider? time = null, string? executablePath = null,
		TessynDesktop.Services.IAppSettingsService? appSettings = null)
	{
		_launcher       = launcher ?? new CodexProcessLauncher();
		_locator        = locator ?? new CodexExecutableLocator();
		_time           = time ?? TimeProvider.System;
		_fixedPath      = executablePath;
		_appSettings    = appSettings;
		_clientVersion  = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? typeof(CodexHostPool).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
	}

	/// <summary>The hosts created so far, one per profile.</summary>
	public IReadOnlyList<CodexAppServerHost> Hosts
	{
		get { lock (_gate) return _hosts.Values.ToList(); }
	}

	/// <summary>Raised once for each new host, before it is handed out (the engine subscribes to its notifications there).</summary>
	public event Action<CodexAppServerHost>? HostCreated;

	/// <summary>
	/// The host of <paramref name="profile"/>, created on first use. A profile id always maps to one home: asking for it with another home is
	/// an error, because two processes on one thread can't share it (FR.33.6).
	/// </summary>
	/// <exception cref="CodexNotFoundException">Codex is not installed or the configured path is unusable (FR.33.17).</exception>
	public CodexAppServerHost GetHost(CodexProfileRef profile)
	{
		CodexAppServerHost? created = null;
		CodexAppServerHost host;
		lock (_gate)
		{
			if (_hosts.TryGetValue(profile.ProfileId, out var existing))
			{
				if (!string.Equals(existing.CodexHome ?? string.Empty, profile.CodexHome ?? string.Empty, StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException($"Codex profile {profile.ProfileId} already runs on another home folder.");
				return existing;
			}

			var executable = ResolveExecutableLocked();
			host = new CodexAppServerHost(new CodexHostOptions
			{
				ExecutablePath = executable,
				ProfileId      = profile.ProfileId,
				CodexHome      = profile.CodexHome,
				ClientVersion  = _clientVersion,
			}, _launcher, _time);
			_hosts[profile.ProfileId] = host;
			created = host;
		}

		_log.Information("Codex host for profile {Profile} created ({Home})", profile.ProfileId, profile.CodexHome ?? "Codex Default");
		try
		{
			HostCreated?.Invoke(created);
		}
		catch (Exception ex)
		{
			_log.Error(ex, "A Codex host-created handler failed");
		}

		return host;
	}

	/// <summary>Stops the profile's host and forgets it (profile delete, FR.33.6). The next need creates a new one.</summary>
	public async Task StopHostAsync(string profileId)
	{
		CodexAppServerHost? host;
		lock (_gate)
		{
			if (_hosts.Remove(profileId, out host) == false)
				return;
		}

		await host.DisposeAsync().ConfigureAwait(false);
	}

	/// <summary>Kills every host's process at once ("Terminate all", FR.15.5, daemon exit). The hosts stay, and start again on the next need.</summary>
	public void TerminateAll()
	{
		var hosts = Hosts;
		if (hosts.Count == 0)
			return;

		var all = Task.WhenAll(hosts.Select(TerminateQuietlyAsync));
		if (!all.Wait(Constants.Codex.StopGrace))
			_log.Warning("Terminating {Count} Codex hosts took longer than {Grace}", hosts.Count, Constants.Codex.StopGrace);
	}

	public async ValueTask DisposeAsync()
	{
		foreach (var host in Hosts)
			await host.DisposeAsync().ConfigureAwait(false);
	}

	private static async Task TerminateQuietlyAsync(CodexAppServerHost host)
	{
		try
		{
			await host.TerminateAsync().ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Terminating the Codex host of {Profile} failed", host.ProfileId);
		}
	}

	private string ResolveExecutableLocked()
	{
		if (_fixedPath is not null)
			return _fixedPath;

		string? configured = null;
		if (_appSettings is not null)
		{
			lock (_appSettings.SyncRoot)
				configured = string.IsNullOrWhiteSpace(_appSettings.Settings.CodexPath) ? null : _appSettings.Settings.CodexPath.Trim();
		}

		if (_executablePath is not null && string.Equals(_locatedFor, configured ?? string.Empty, StringComparison.OrdinalIgnoreCase))
			return _executablePath;

		var located = _locator.Locate(configured);
		if (!located.Found)
			throw new CodexNotFoundException(located.Problem ?? Constants.Codex.Texts.NotFound);

		_executablePath = located.ExecutablePath!;
		_locatedFor     = configured ?? string.Empty;
		if (!string.Equals(_versionCheckedFor, _executablePath, StringComparison.OrdinalIgnoreCase))
		{
			_versionCheckedFor = _executablePath;
			_ = LogVersionAsync(_executablePath);
		}

		return _executablePath;
	}

	private async Task LogVersionAsync(string executable)
	{
		try
		{
			var version = await _locator.ReadVersionAsync(executable).ConfigureAwait(false);
			if (version is null)
			{
				_log.Warning("Could not read the version of Codex at {Path}", executable);
				return;
			}

			if (_locator.VersionNote(version) is { } note)
				_log.Warning("Codex {Version} at {Path}: {Note}", version, executable, note);
			else
				_log.Information("Codex {Version} at {Path}", version, executable);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			_log.Warning(ex, "Could not read the version of Codex at {Path}", executable);
		}
	}
}
