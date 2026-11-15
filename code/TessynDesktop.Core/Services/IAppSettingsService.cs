using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// Loads and saves application settings to appsettings.json atomically.
/// Single source of truth for all persistent application state.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IAppSettingsService
{
	AppSettingsModel Settings { get; }
	void Load();
	void Save();

	/// <summary>
	/// Raised after every <see cref="Save"/> has written the file, on the saving thread and outside the service's own lock (the caller may still
	/// hold <see cref="SyncRoot"/>). Subscribers must return quickly and never throw into the saver. The default does nothing, so a stand-in
	/// for the service need not implement it. Package J3 uses it for per-section change detection (<c>settings.changed</c>).
	/// </summary>
	event System.Action? Saved
	{
		add { }
		remove { }
	}

	/// <summary>
	/// Shared lock guarding <see cref="Save"/>/<see cref="Load"/> and all in-memory mutations of
	/// <see cref="Settings"/>'s tree (ISSUE — concurrent-settings-save-lost-update). Callers that
	/// mutate the shared <see cref="Settings"/> graph from a background/worker thread and then call
	/// <see cref="Save"/> must hold this lock across both the mutation and the Save call — e.g.
	/// <c>SessionRegistry</c>'s tree-mutation methods. <c>lock</c> (Monitor) is reentrant on the same
	/// thread, so calling <see cref="Save"/> while already holding this lock does not deadlock.
	/// </summary>
	object SyncRoot { get; }
}
