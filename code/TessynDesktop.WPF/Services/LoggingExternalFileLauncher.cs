using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// <see cref="IExternalFileLauncher"/> of a <c>--test-window</c> run: starts nothing (a media player or document viewer would open on the user's
/// desktop), applies the same two gates as <see cref="SystemExternalFileLauncher"/> only as far as the allowlist, and logs what it was asked to open.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class LoggingExternalFileLauncher : IExternalFileLauncher
{
	private readonly ExternalOpenPolicy _policy;
	private readonly ILogger<LoggingExternalFileLauncher> _logger;

	public LoggingExternalFileLauncher(ExternalOpenPolicy policy, ILogger<LoggingExternalFileLauncher> logger)
	{
		_policy = policy;
		_logger = logger;
	}

	public bool TryOpen(string path)
	{
		if (!_policy.IsAllowed(path) || !File.Exists(path))
			return false;

		_logger.LogInformation("External open requested (test window, nothing started): {Extension} file of {Size} bytes", Path.GetExtension(path), new FileInfo(path).Length);
		return true;
	}
}
