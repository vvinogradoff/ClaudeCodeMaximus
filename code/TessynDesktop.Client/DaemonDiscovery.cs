using System.Diagnostics;
using System.Text.Json;
using TessynDesktop.Models.Remote;
using TessynDesktop.Services;

namespace TessynDesktop.Client;

/// <summary>
/// Finds the running daemon: reads <c>%AppData%\TessynDesktop\daemon.json</c> (endpoint, pid, run id, token file) and the token
/// from the DPAPI-protected file it names. A file left behind by a crash (its pid no longer runs) is reported as stale.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonDiscovery
{
	private const string DataFolderName = "TessynDesktop";

	private readonly string _filePath;

	/// <summary>Discovery at the default location, <c>%AppData%\TessynDesktop\daemon.json</c>.</summary>
	public DaemonDiscovery() : this(GetDefaultPath())
	{
	}

	/// <summary>Discovery at a custom <c>daemon.json</c> path (tests, portable setups).</summary>
	public DaemonDiscovery(string daemonFilePath)
	{
		_filePath = daemonFilePath;
	}

	/// <summary>The default <c>daemon.json</c> path.</summary>
	public static string GetDefaultPath()
	{
		var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		return Path.Combine(appData, DataFolderName, RemoteProtocol.Discovery.FileName);
	}

	/// <summary>Reads the discovery file and the token. Never throws for an unusable file; see <see cref="DaemonDiscoveryStatus"/>.</summary>
	public async Task<DaemonDiscoveryResult> DiscoverAsync(CancellationToken cancellationToken = default)
	{
		if (!File.Exists(_filePath))
			return new DaemonDiscoveryResult(DaemonDiscoveryStatus.NoDaemonFile, null);

		var model = await ReadModelAsync(cancellationToken).ConfigureAwait(false);
		if (model == null
			|| string.IsNullOrWhiteSpace(model.TokenFile)
			|| string.IsNullOrWhiteSpace(model.RunId)
			|| !Uri.TryCreate(model.Endpoint, UriKind.Absolute, out var endpoint))
			return new DaemonDiscoveryResult(DaemonDiscoveryStatus.InvalidFile, null);

		if (!IsRunning(model.Pid))
			return new DaemonDiscoveryResult(DaemonDiscoveryStatus.StaleFile, null);

		var token = OperatingSystem.IsWindows() ? DpapiTokenReader.Read(model.TokenFile) : null;
		if (token == null)
			return new DaemonDiscoveryResult(DaemonDiscoveryStatus.TokenUnavailable, null);

		return new DaemonDiscoveryResult(DaemonDiscoveryStatus.Found, new DaemonEndpoint(endpoint, token, model.RunId, model.Pid));
	}

	private async Task<DaemonDiscoveryFileModel?> ReadModelAsync(CancellationToken cancellationToken)
	{
		try
		{
			await using var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			return await JsonSerializer.DeserializeAsync<DaemonDiscoveryFileModel>(stream, TdcpJsonOptions.Default, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
		{
			return null;
		}
	}

	private static bool IsRunning(int pid)
	{
		if (pid <= 0)
			return false;

		try
		{
			using var process = Process.GetProcessById(pid);
			return !process.HasExited;
		}
		catch (ArgumentException)
		{
			return false;
		}
		catch (InvalidOperationException)
		{
			return false;
		}
		catch (System.ComponentModel.Win32Exception)
		{
			// Exists but cannot be inspected (e.g. elevated): treat as running rather than discarding a valid file.
			return true;
		}
	}
}
