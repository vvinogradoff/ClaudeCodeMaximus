using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The custom title bar (FR.6.3): the chevron that auto-hides the pane that holds Sessions (FR.6.15, remembered with the layout), the menu, the day/night toggle, the build
/// number and the warning shown when the app runs from a build output folder. Contains no WPF types.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class TitleBarViewModel : ObservableObject
{
	private readonly IAppearanceService _appearance;
	private readonly IClientSettingsStore _settings;
	private readonly IShellCommands _commands;
	private readonly IShellLayout _layout;
	private readonly IUiDispatcher _ui;
	private readonly IPreviewRouter? _previews;

	[ObservableProperty]
	private bool _isDark;

	public TitleBarViewModel(IAppearanceService appearance, IClientSettingsStore settings, IShellCommands commands, IShellLayout layout, IUiDispatcher ui, string buildNumber, bool isBuildOutput, IPreviewRouter? previews = null)
	{
		_previews = previews;
		_appearance = appearance;
		_settings = settings;
		_commands = commands;
		_layout = layout;
		_ui = ui;
		BuildNumber = buildNumber;
		IsBuildOutput = isBuildOutput;
		_isDark = appearance.IsDark;
		layout.Changed += (_, _) => _ui.Post(RaiseTreeState);
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

	/// <summary>
	/// The preview windows mode is on: the toggle looks checked. Settable, so that the button works through the Toggle pattern of UI Automation too (a screen reader, a test),
	/// which flips a ToggleButton without running its command.
	/// </summary>
	public bool IsPreviewWindowsOn
	{
		get => _previews?.IsActive ?? false;
		set
		{
			if (IsMonitorToggleEnabled && value != IsPreviewWindowsOn)
				_previews?.SetEnabled(value);
			else
				OnPropertyChanged();   // refused: the button goes back to the real state
		}
	}

	public string MonitorToggleToolTip => _previews switch
	{
		null or { CanToggle: false } => Constants.PreviewWindow.MonitorDisabledToolTip,
		{ IsEnabled: true } => Constants.PreviewWindow.MonitorOnToolTip,
		_ => Constants.PreviewWindow.MonitorOffToolTip,
	};

	/// <summary>The pane that holds Sessions is shown (not auto-hidden); the chevron's icon follows it (FR.6.15).</summary>
	public bool IsTreeVisible => !_layout.IsAutoHidden(Constants.DockedShell.SessionsPanelId);

	/// <summary>The chevron works while the Sessions panel is open and docked; it is disabled when it is closed or floating (FR.6.15).</summary>
	public bool CanToggleTree => _layout.CanToggleAutoHide(Constants.DockedShell.SessionsPanelId);

	[RelayCommand(CanExecute = nameof(CanToggleTree))]
	private void ToggleTree() => _layout.ToggleAutoHide(Constants.DockedShell.SessionsPanelId);

	private void RaiseTreeState()
	{
		OnPropertyChanged(nameof(IsTreeVisible));
		OnPropertyChanged(nameof(CanToggleTree));
		ToggleTreeCommand.NotifyCanExecuteChanged();
	}

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

	/// <summary>Shows (expands) or hides (auto-hides) the pane that holds Sessions; the layout remembers it.</summary>
	public void SetTreeVisible(bool visible)
	{
		if (visible == IsTreeVisible)
			return;

		_layout.ToggleAutoHide(Constants.DockedShell.SessionsPanelId);
	}
}
