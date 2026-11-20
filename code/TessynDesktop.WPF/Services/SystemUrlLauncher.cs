using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace TessynDesktop.WPF.Services;

/// <summary><see cref="IUrlLauncher"/> that hands http(s) URLs to the shell.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SystemUrlLauncher : ISystemUrlLauncher
{
	private readonly UrlPolicy _policy;
	private readonly ILogger<SystemUrlLauncher> _logger;
	private readonly bool _logOnly;

	/// <param name="logOnly">True under <c>--test-window</c>: a test window never starts a browser, it only logs the address (<see cref="Constants.Browser.TestWindowOpenLog"/>).</param>
	public SystemUrlLauncher(UrlPolicy policy, ILogger<SystemUrlLauncher> logger, bool logOnly = false)
	{
		_policy = policy;
		_logger = logger;
		_logOnly = logOnly;
	}

	public bool TryOpen(string? url)
	{
		if (!_policy.TryParse(url, out var uri))
		{
			_logger.LogInformation("Refused to open a non-http(s) URL");
			return false;
		}

		if (_logOnly)
		{
			_logger.LogInformation(Constants.Browser.TestWindowOpenLog, uri.AbsoluteUri);
			return true;
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
