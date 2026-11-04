using System;
using System.Diagnostics;
using Serilog;

namespace TessynDesktop.Services;

/// <summary>
/// Opens a Session Tag's URL in the default OS browser (FR.24.7). Only URLs that pass
/// <see cref="SessionTagUrlPolicy.IsOpenable"/> are shell-executed; anything else is logged and ignored.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SessionTagLauncher
{
	private static readonly ILogger _log = Log.ForContext<SessionTagLauncher>();

	public void Open(string url)
	{
		if (!SessionTagUrlPolicy.IsOpenable(url))
		{
			_log.Warning("Session tag URL ignored (not an openable https linear.app / github.com URL): {Url}", url);
			return;
		}

		try
		{
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Could not open session tag URL {Url}", url);
		}
	}
}
