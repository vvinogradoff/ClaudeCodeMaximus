using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.Switch;

namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>
/// The Startup tab: "Start the background daemon when none is running" (client settings section <c>daemonHost</c>, off by default). Off means this client waits for the
/// Avalonia app to bring the daemon up; on means it starts the background host itself when it finds none. "Start daemon elevated" (FR.32.10) is saved with it;
/// the daemon's way of running and the two restart buttons (FR.32.11, FR.32.12) act at once and are not part of Save.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class StartupSectionViewModel : SettingsSectionViewModel, ISettingsEditor
{
	private readonly IClientSettingsStore _store;
	private readonly IDaemonModeControl? _daemon;

	[ObservableProperty]
	private bool _startBackgroundDaemon;

	[ObservableProperty]
	private bool _startDaemonElevated;

	/// <param name="daemon">The connection's daemon control; null hides the status and the restart buttons (a test or custom environment).</param>
	public StartupSectionViewModel(IClientSettingsStore store, IDaemonModeControl? daemon = null)
	{
		_store = store;
		_daemon = daemon;
		if (daemon != null)
			PropertyChangedEventManager.AddHandler(daemon, OnDaemonChanged, string.Empty);   // weak: a Settings window comes and goes, the connection stays
		Read();
	}

	public override string Title => "Startup";

	public bool HasChanges
	{
		get
		{
			var saved = _store.Get<DaemonHostSettings>();
			return StartBackgroundDaemon != saved.StartBackgroundDaemon || StartDaemonElevated != saved.StartDaemonElevated;
		}
	}

	public string Hint => Constants.Settings.StartBackgroundDaemonHint;

	public string ElevatedHint => Constants.Settings.StartDaemonElevatedHint;

	public string RestartHint => Constants.Settings.RestartDaemonModeHint;

	/// <summary>True when the status and the restart buttons are shown.</summary>
	public bool HasDaemonControl => _daemon != null;

	/// <summary>FR.32.12: which way the connected daemon runs; null while it is not known.</summary>
	public string? DaemonStatusText => _daemon?.DaemonElevated switch
	{
		true => Constants.Settings.DaemonRunsElevated,
		false => Constants.Settings.DaemonRunsNormally,
		null => null,
	};

	public string? RestartNote => _daemon?.RestartNote;

	public Task<bool> SaveAsync()
	{
		ClearMessages();
		var background = StartBackgroundDaemon;
		var elevated = StartDaemonElevated;
		_store.Update<DaemonHostSettings>(s =>
		{
			s.StartBackgroundDaemon = background;
			s.StartDaemonElevated = elevated;
		});
		StatusText = Constants.Settings.Saved;
		return Task.FromResult(true);
	}

	[RelayCommand(CanExecute = nameof(CanRestartDaemon))]
	private Task RestartDaemonElevatedAsync() => _daemon?.SwitchDaemonAsync(HostStartMode.Elevated) ?? Task.CompletedTask;

	[RelayCommand(CanExecute = nameof(CanRestartDaemon))]
	private Task RestartDaemonNormalAsync() => _daemon?.SwitchDaemonAsync(HostStartMode.Normal) ?? Task.CompletedTask;

	private bool CanRestartDaemon() => _daemon?.CanRestartDaemon == true;

	private void OnDaemonChanged(object? sender, PropertyChangedEventArgs e)
	{
		OnPropertyChanged(nameof(DaemonStatusText));
		OnPropertyChanged(nameof(RestartNote));
		RestartDaemonElevatedCommand.NotifyCanExecuteChanged();
		RestartDaemonNormalCommand.NotifyCanExecuteChanged();
	}

	[RelayCommand]
	public Task ReloadAsync()
	{
		ClearMessages();
		Read();
		return Task.CompletedTask;
	}

	public void Read()
	{
		var saved = _store.Get<DaemonHostSettings>();
		StartBackgroundDaemon = saved.StartBackgroundDaemon;
		StartDaemonElevated = saved.StartDaemonElevated;
	}
}
