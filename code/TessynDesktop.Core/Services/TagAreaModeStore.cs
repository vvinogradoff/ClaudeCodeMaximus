namespace TessynDesktop.Services;

/// <summary>
/// The one global tag area mode (FR.24.16): "full" (true) or "mini" (false), remembered in the app settings. Reads the setting each time, so
/// every session view model sees the current value; <see cref="Changed"/> tells the views of the others.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class TagAreaModeStore
{
	private readonly IAppSettingsService _settings;

	public TagAreaModeStore(IAppSettingsService settings) => _settings = settings;

	public bool IsExpanded
	{
		get
		{
			lock (_settings.SyncRoot)
				return _settings.Settings.TagAreaExpanded;
		}
	}

	/// <summary>Raised after the mode changed and was saved.</summary>
	public event System.Action? Changed;

	/// <summary>Sets and persists the mode; does nothing when it is already that value.</summary>
	public void Set(bool expanded)
	{
		lock (_settings.SyncRoot)
		{
			if (_settings.Settings.TagAreaExpanded == expanded)
				return;

			_settings.Settings.TagAreaExpanded = expanded;
			_settings.Save();
		}

		Changed?.Invoke();
	}
}
