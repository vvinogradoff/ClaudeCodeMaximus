namespace TessynDesktop.Models;

/// <summary>Who cancelled an elevated run (FR.15.18): the agent withdrew it while pending, or the user stopped it while running.</summary>
/// <remarks>Created by Claude</remarks>
public enum ElevatedRunCancelledBy
{
	Agent,
	User,
}
