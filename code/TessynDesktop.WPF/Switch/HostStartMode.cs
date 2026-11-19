namespace TessynDesktop.WPF.Switch;

/// <summary>How the background host is started (FR.32.10, FR.32.11).</summary>
/// <remarks>Created by Claude</remarks>
public enum HostStartMode
{
	/// <summary>Started normally, as this client runs.</summary>
	Normal,

	/// <summary>Started through the Windows administrator prompt: the host and everything it starts run as administrator.</summary>
	Elevated,
}
