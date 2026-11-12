namespace TessynDesktop.Models;

/// <summary>Lifecycle state of the relay connector.</summary>
/// <remarks>Created by Claude</remarks>
public enum RelayState
{
	Disabled,
	Connecting,
	Connected,
	Error,
}
