using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>The daemon's own vital signs for <c>daemon.health</c>: counters only, no path and no setting.</summary>
/// <remarks>Created by Claude</remarks>
public interface IDaemonHealthService
{
	/// <summary>Reads the health now.</summary>
	DaemonHealthResult Get();
}
