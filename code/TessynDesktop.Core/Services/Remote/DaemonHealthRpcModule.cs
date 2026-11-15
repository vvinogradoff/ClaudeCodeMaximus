using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// <c>daemon.health</c> (package J4): the uptime, run id, event head, running sessions, index state, log size and memory of the daemon.
/// Loopback only (<c>remoteCallable: false</c>); the answer is counters, never a path or a setting.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class DaemonHealthRpcModule : IRemoteRpcModule
{
	private readonly IDaemonHealthService _health;

	public DaemonHealthRpcModule(IDaemonHealthService health)
	{
		_health = health;
	}

	/// <inheritdoc />
	public void Register(TdcpMethodRegistry registry) =>
		registry.AddSync(RemoteProtocol.Health.Method, _ => _health.Get(), remoteCallable: false);
}
