namespace TessynDesktop.WPF.State;

/// <summary>Owner of the client state: loads it at startup and writes changes through a debounced single writer.</summary>
/// <remarks>Created by Claude</remarks>
public interface IClientStateStore
{
	/// <summary>The current state (valid after <see cref="LoadAsync"/>); read on the UI thread, change it only through <see cref="Update"/>.</summary>
	ClientStateModel State { get; }

	/// <summary>Reads the state file; a missing or unreadable file yields defaults.</summary>
	Task LoadAsync();

	/// <summary>
	/// Reads under the same lock that <see cref="Update"/> writes under, so a read never sees a change half-applied. The default reads without a
	/// lock (single-threaded fakes); the real store locks.
	/// </summary>
	T Read<T>(Func<ClientStateModel, T> read) => read(State);

	/// <summary>Applies a change and schedules a debounced write.</summary>
	void Update(Action<ClientStateModel> change);

	/// <summary>Writes pending changes now (at shutdown).</summary>
	Task FlushAsync();
}
