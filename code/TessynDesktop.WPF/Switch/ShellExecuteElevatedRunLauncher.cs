using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Switch;

/// <summary>
/// The real <see cref="IElevatedRunLauncher"/> (FR.32.16): <c>ShellExecute</c> with the verb the daemon names (<c>runas</c>), hidden, with no redirected pipes (the wrapper writes
/// its own files). The call opens the Windows administrator prompt and blocks until it is answered, so it runs on a background thread and never holds the UI.
/// A declined prompt is the Win32 error 1223 (<see cref="Constants.Switch.ElevationCancelledError"/>).
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ShellExecuteElevatedRunLauncher(ILogger<ShellExecuteElevatedRunLauncher> logger) : IElevatedRunLauncher
{
	public Task<ElevatedLaunchResult> LaunchAsync(ElevatedLaunchDto launch, CancellationToken cancellationToken = default) =>
		Task.Run(() => Start(launch), CancellationToken.None);

	private ElevatedLaunchResult Start(ElevatedLaunchDto launch)
	{
		try
		{
			var info = new ProcessStartInfo
			{
				FileName = launch.FileName,
				Arguments = launch.Arguments,
				WorkingDirectory = launch.WorkingDirectory,
				Verb = launch.Verb,
				UseShellExecute = true,
				WindowStyle = ProcessWindowStyle.Hidden,
			};
			using var process = Process.Start(info);
			logger.LogInformation("Elevated run {Id}: started through the Windows prompt", launch.RequestId);
			return ElevatedLaunchResult.Started;
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == Constants.Switch.ElevationCancelledError)
		{
			logger.LogInformation("Elevated run {Id}: the Windows prompt was declined", launch.RequestId);
			return ElevatedLaunchResult.Declined;
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Elevated run {Id}: the start failed", launch.RequestId);
			return ElevatedLaunchResult.Failed(ex.Message);
		}
	}
}
