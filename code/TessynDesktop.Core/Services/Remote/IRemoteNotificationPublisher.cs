namespace TessynDesktop.Services;

/// <summary>
/// Turns the daemon's domain events into the remote notifications of contract section 4 and publishes them on the
/// <see cref="IRemoteEventBus"/>. Every handler is O(1): anything that needs a lookup or a state read is deferred to the
/// bus's coalescing timer.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IRemoteNotificationPublisher
{
	/// <summary>Subscribes to the daemon events. Idempotent; never throws.</summary>
	void Start();

	/// <summary>Unsubscribes. Idempotent.</summary>
	void Stop();
}
