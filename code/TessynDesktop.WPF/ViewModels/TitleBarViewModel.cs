using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The custom title bar (FR.6.3): the chevron that collapses the left panel, the menu, the day/night toggle, the build number and the warning shown when the
/// app runs from a build output folder. The collapse is remembered in the client settings. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TitleBarViewModel : ObservableObject
{
	private readonly IAppearanceService _appearance;
	private readonly IClientSettingsStore _settings;
	private readonly IShellCommands _commands;
	private readonly IUiDispatcher _ui;
	private readonly IPreviewRouter? _previews;

	[ObservableProperty]
	private bool _isTreeVisible;

	[ObservableProperty]
	private bool _isDark;

	public TitleBarViewModel(IAppearanceService appearance, IClientSettingsStore settings, IShellCommands commands, IUiDispatcher ui, string buildNumber, bool isBuildOutput, IPreviewRouter? previews = null)
	{
		_previews = previews;
		_appearance = appearance;
		_settings = settings;
		_commands = commands;
		_ui = ui;
		BuildNumber = buildNumber;
		IsBuildOutput = isBuildOutput;
		_isTreeVisible = !settings.Get<ShellLayoutSettings>().TreeCollapsed;
		_isDark = appearance.IsDark;
		appearance.Changed += (_, _) => _ui.Post(() => IsDark = _appearance.IsDark);
		if (previews != null)
			previews.StateChanged += (_, _) => _ui.Post(RaisePreviewState);
	}

	/// <summary>Build time of this app as <c>yyyyMM.dd.HHmm</c>; empty when it cannot be read.</summary>
	public string BuildNumber { get; }

	/// <summary>The app runs from <c>bin\Debug</c> or <c>bin\Release</c>, so it is never updated on restart.</summary>
	public bool IsBuildOutput { get; }

	public string BuildOutputWarning => Constants.TitleBar.BuildOutputWarning;

	/// <summary>The Monitor toggle exists only where a preview router is registered (FR.6.10).</summary>
	public bool IsMonitorToggleVisible => _previews != null;

	/// <summary>Two or more monitors are connected (FR.29.1).</summary>
	public bool IsMonitorToggleEnabled => _previews?.CanToggle ?? false;

	/// <summary>The preview windows mode is on: the toggle looks checked.</summary>
	public bool IsPreviewWindowsOn => _previews?.IsActive ?? false;

	public string MonitorToggleToolTip => _previews switch
	{
		null or { CanToggle: false } => Constants.PreviewWindow.MonitorDisabledToolTip,
		{ IsEnabled: true } => Constants.PreviewWindow.MonitorOnToolTip,
		_ => Constants.PreviewWindow.MonitorOffToolTip,
	};

	/// <summary>The left panel was shown or hidden (the window lays itself out).</summary>
	public event EventHandler? TreeVisibilityChanged;

	[RelayCommand]
	private void ToggleTree() => SetTreeVisible(!IsTreeVisible);

	[RelayCommand(CanExecute = nameof(IsMonitorToggleEnabled))]
	private void TogglePreviewWindows() => _previews?.SetEnabled(!_previews.IsEnabled);

	private void RaisePreviewState()
	{
		OnPropertyChanged(nameof(IsMonitorToggleEnabled));
		OnPropertyChanged(nameof(IsPreviewWindowsOn));
		OnPropertyChanged(nameof(MonitorToggleToolTip));
		TogglePreviewWindowsCommand.NotifyCanExecuteChanged();
	}

	[RelayCommand]
	private void ToggleTheme() => _appearance.ToggleTheme();

	[RelayCommand]
	private Task OpenSettingsAsync() => _commands.ExecuteAsync(Constants.Settings.OpenCommandId, null);

	/// <summary>Shows or hides the left panel and remembers it.</summary>
	public void SetTreeVisible(bool visible)
	{
		if (visible == IsTreeVisible)
			return;

		IsTreeVisible = visible;
		_settings.Update<ShellLayoutSettings>(s => s.TreeCollapsed = !visible);
		TreeVisibilityChanged?.Invoke(this, EventArgs.Empty);
	}
}
