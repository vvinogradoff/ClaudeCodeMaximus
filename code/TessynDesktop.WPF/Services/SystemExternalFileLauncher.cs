using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Infrastructure;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// <see cref="IExternalFileLauncher"/> over the shell. Two gates before anything starts: the file's type must be on the
/// <see cref="ExternalOpenPolicy"/> allowlist, and the file must lie inside the preview cache (a file this app wrote itself, never
/// an arbitrary path from the daemon or a transcript).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SystemExternalFileLauncher : IExternalFileLauncher
{
	private readonly ExternalOpenPolicy _policy;
	private readonly AppPaths _paths;
	private readonly ILogger<SystemExternalFileLauncher> _logger;

	public SystemExternalFileLauncher(ExternalOpenPolicy policy, AppPaths paths, ILogger<SystemExternalFileLauncher> logger)
	{
		_policy = policy;
		_paths = paths;
		_logger = logger;
	}

	public bool TryOpen(string path)
	{
		if (!_policy.IsAllowed(path))
		{
			_logger.LogWarning("Refused to open a file of a type that is not on the allowlist");
			return false;
		}

		var full = Path.GetFullPath(path);
		var root = Path.GetFullPath(_paths.PreviewCacheDirectory) + Path.DirectorySeparatorChar;
		if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
		{
			_logger.LogWarning("Refused to open a file outside the preview cache");
			return false;
		}

		try
		{
			Process.Start(new ProcessStartInfo(full) { UseShellExecute = true })?.Dispose();
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Opening the preview file failed");
			return false;
		}
	}
}
