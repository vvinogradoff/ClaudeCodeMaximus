using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Startup tab: "Start the background daemon when none is running" (client settings section <c>daemonHost</c>, off by default). Off means this client waits for the
/// Avalonia app to bring the daemon up; on means it starts the background host itself when it finds none.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class StartupSectionViewModel : SettingsSectionViewModel, ISettingsEditor
{
	private readonly IClientSettingsStore _store;

	[ObservableProperty]
	private bool _startBackgroundDaemon;

	public StartupSectionViewModel(IClientSettingsStore store)
	{
		_store = store;
		Read();
	}

	public override string Title => "Startup";

	public bool HasChanges => StartBackgroundDaemon != _store.Get<DaemonHostSettings>().StartBackgroundDaemon;

	public string Hint => Constants.Settings.StartBackgroundDaemonHint;

	public Task<bool> SaveAsync()
	{
		ClearMessages();
		var edited = StartBackgroundDaemon;
		_store.Update<DaemonHostSettings>(s => s.StartBackgroundDaemon = edited);
		StatusText = Constants.Settings.Saved;
		return Task.FromResult(true);
	}

	[RelayCommand]
	public Task ReloadAsync()
	{
		ClearMessages();
		Read();
		return Task.CompletedTask;
	}

	public void Read() => StartBackgroundDaemon = _store.Get<DaemonHostSettings>().StartBackgroundDaemon;
}
