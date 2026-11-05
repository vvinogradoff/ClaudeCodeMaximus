namespace TessynDesktop.Services;

/// <summary>
/// Keeps the search index live (FR.1.13): re-indexes a session shortly after each of its turns completes.
/// Holds an event subscription, so it must be a singleton that is started once and stopped on shutdown.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISessionIndexUpdater
{
	/// <summary>Subscribes to turn completion. Idempotent.</summary>
	void Start();

	/// <summary>Unsubscribes and cancels pending re-indexing. Idempotent.</summary>
	void Stop();
}
