using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>Reads the live tree for the remote modules: one pass under the settings lock, copies only.</summary>
/// <remarks>Created by Claude</remarks>
public interface IRemoteTreeReader
{
	/// <summary>Copies the whole tree. Holds <see cref="IAppSettingsService.SyncRoot"/> only for the copy (no I/O inside). Thread-safe.</summary>
	RemoteTreeSnapshotModel Snapshot();
}
