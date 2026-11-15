namespace TessynDesktop.WPF.State;

/// <summary>
/// Versioned, typed sections inside <c>wpf.json</c> (theme, fonts, key bindings, screensaver, later ones). A package adds a section by
/// adding a class with <see cref="ClientSettingsSectionAttribute"/>; <c>ClientStateModel</c> never changes again. Writes go through the
/// debounced single writer of <see cref="IClientStateStore"/>.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IClientSettingsStore
{
	/// <summary>Raised after a section was written, on the thread that wrote it.</summary>
	event EventHandler<ClientSettingsChangedEventArgs>? Changed;

	/// <summary>A copy of the section; defaults when it was never written, is unreadable or comes from a newer version.</summary>
	T Get<T>() where T : class, new();

	/// <summary>Applies <paramref name="change"/> to a copy of the section, stores it at the current version and schedules a write.</summary>
	void Update<T>(Action<T> change) where T : class, new();
}
