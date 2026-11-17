using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Carries the date format of the session rows (Settings, Appearance) into <see cref="SessionDisplay"/> and tells the lists when it changes, so the rows show the
/// new format at once.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ListDisplayService
{
	private readonly IClientSettingsStore _store;
	private readonly IUiDispatcher _ui;

	public ListDisplayService(IClientSettingsStore store, IUiDispatcher ui)
	{
		_store = store;
		_ui = ui;
	}

	/// <summary>The date format changed; raised on the UI thread.</summary>
	public event EventHandler? Changed;

	/// <summary>Reads the stored format and follows it; call once before the window is shown.</summary>
	public void Start()
	{
		SessionDisplay.DateFormat = _store.Get<AppearanceSettings>().DateFormat;
		_store.Changed += (_, e) =>
		{
			if (e.Section != Constants.SettingsSections.Appearance)
				return;

			var format = _store.Get<AppearanceSettings>().DateFormat;
			if (format == SessionDisplay.DateFormat)
				return;

			SessionDisplay.DateFormat = format;
			_ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
		};
	}
}
