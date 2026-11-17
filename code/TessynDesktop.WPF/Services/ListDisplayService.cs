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

	/// <summary>The date format or the recency colour of the closed Recent dropdown changed; raised on the UI thread.</summary>
	public event EventHandler? Changed;

	/// <summary>Whether the closed Recent dropdown shows the open session's recency colour (Settings, Appearance; on by default).</summary>
	public bool ShowRecencyInRecentDropdown { get; private set; } = true;

	/// <summary>Reads the stored format and follows it; call once before the window is shown.</summary>
	public void Start()
	{
		var stored = _store.Get<AppearanceSettings>();
		SessionDisplay.DateFormat = stored.DateFormat;
		ShowRecencyInRecentDropdown = stored.ShowRecencyInRecentDropdown;
		_store.Changed += (_, e) =>
		{
			if (e.Section != Constants.SettingsSections.Appearance)
				return;

			var settings = _store.Get<AppearanceSettings>();
			if (settings.DateFormat == SessionDisplay.DateFormat && settings.ShowRecencyInRecentDropdown == ShowRecencyInRecentDropdown)
				return;

			SessionDisplay.DateFormat = settings.DateFormat;
			ShowRecencyInRecentDropdown = settings.ShowRecencyInRecentDropdown;
			_ui.Post(() => Changed?.Invoke(this, EventArgs.Empty));
		};
	}
}
