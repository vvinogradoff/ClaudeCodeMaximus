using System.ComponentModel;

namespace TessynDesktop.WPF.Switch;

/// <summary>Which way the connected daemon runs, and the restart that changes it (FR.32.11, FR.32.12); the Settings tab Startup reads and drives it.</summary>
/// <remarks>Created by Claude</remarks>
public interface IDaemonModeControl : INotifyPropertyChanged
{
	/// <summary>True when the connected daemon runs elevated, false when it runs normally; null while no daemon is connected or when it does not report it.</summary>
	bool? DaemonElevated { get; }

	/// <summary>True when the daemon can be restarted from here: a connected daemon and the real launcher.</summary>
	bool CanRestartDaemon { get; }

	/// <summary>Why the last restart did not bring a new daemon up; null when there is nothing to say.</summary>
	string? RestartNote { get; }

	/// <summary>Asks first, ends the daemon and starts the host <paramref name="mode"/>'s way; the client reconnects by itself.</summary>
	Task SwitchDaemonAsync(HostStartMode mode);
}
