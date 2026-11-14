using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Services;

/// <summary><see cref="IUrlLauncher"/> that hands http(s) URLs to the shell.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SystemUrlLauncher : IUrlLauncher
{
	private readonly UrlPolicy _policy;
	private readonly ILogger<SystemUrlLauncher> _logger;

	public SystemUrlLauncher(UrlPolicy policy, ILogger<SystemUrlLauncher> logger)
	{
		_policy = policy;
		_logger = logger;
	}

	public bool TryOpen(string? url)
	{
		if (!_policy.TryParse(url, out var uri))
		{
			_logger.LogInformation("Refused to open a non-http(s) URL");
			return false;
		}

		try
		{
			Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Opening {Url} failed", uri.AbsoluteUri);
			return false;
		}
	}
}
