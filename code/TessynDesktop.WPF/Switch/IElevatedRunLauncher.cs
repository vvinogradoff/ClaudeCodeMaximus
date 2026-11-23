using TessynDesktop.Models.Remote;

namespace TessynDesktop.WPF.Switch;

/// <summary>
/// Starts the process of an approved elevated run (FR.32.16, FR.15.19): exactly the launch description the daemon built, nothing added and nothing changed.
/// The real implementation goes through the Windows administrator prompt; the fake of the test window starts the same description unelevated.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IElevatedRunLauncher
{
	/// <summary>Starts <paramref name="launch"/> and returns when the process has started or the start came to nothing. Never throws for a declined or failed start.</summary>
	Task<ElevatedLaunchResult> LaunchAsync(ElevatedLaunchDto launch, CancellationToken cancellationToken = default);
}
