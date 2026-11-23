namespace TessynDesktop.WPF.Switch;

/// <summary>What starting an elevated run came to (FR.32.16).</summary>
/// <remarks>Created by Claude</remarks>
public enum ElevatedLaunchOutcome
{
	/// <summary>The process was started; the wrapper reports <c>started.json</c> to the daemon from there.</summary>
	Started,

	/// <summary>The User cancelled the Windows administrator prompt (Win32 error 1223).</summary>
	Declined,

	/// <summary>The process could not be started for another reason.</summary>
	Failed,
}
