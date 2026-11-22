using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Switch;

/// <summary>
/// The launcher of a <c>--test-window</c> run with <c>--elevated-launcher=fake:&lt;mode&gt;</c> (A8 Q4): no Windows prompt, no elevation. <see cref="FakeElevatedLauncherMode.Accept"/>
/// starts the same launch description (program, arguments, directory) as a normal hidden process, so the daemon's wrapper runs for real; the other modes answer as a declined
/// prompt, a failed start, or never.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeElevatedRunLauncher(FakeElevatedLauncherMode mode, ILogger<FakeElevatedRunLauncher> logger) : IElevatedRunLauncher
{
	public const string FailReason = "fake launcher: the start failed";

	public FakeElevatedLauncherMode Mode { get; } = mode;

	public async Task<ElevatedLaunchResult> LaunchAsync(ElevatedLaunchDto launch, CancellationToken cancellationToken = default)
	{
		logger.LogInformation("Elevated run {Id}: fake launcher, mode {Mode}", launch.RequestId, Mode);
		switch (Mode)
		{
			case FakeElevatedLauncherMode.Decline:
				return ElevatedLaunchResult.Declined;
			case FakeElevatedLauncherMode.Fail:
				return ElevatedLaunchResult.Failed(FailReason);
			case FakeElevatedLauncherMode.Hang:
				await Task.Delay(Timeout.Infinite, cancellationToken);
				return ElevatedLaunchResult.Failed(FailReason);
			default:
				return await Task.Run(() => StartUnelevated(launch), CancellationToken.None);
		}
	}

	private ElevatedLaunchResult StartUnelevated(ElevatedLaunchDto launch)
	{
		try
		{
			var info = new ProcessStartInfo
			{
				FileName = launch.FileName,
				Arguments = launch.Arguments,
				WorkingDirectory = launch.WorkingDirectory,
				UseShellExecute = false,
				CreateNoWindow = true,
			};
			using var process = Process.Start(info);
			return ElevatedLaunchResult.Started;
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Elevated run {Id}: the fake launcher could not start the process", launch.RequestId);
			return ElevatedLaunchResult.Failed(ex.Message);
		}
	}
}
