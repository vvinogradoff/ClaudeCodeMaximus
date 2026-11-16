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

	[ObservableProperty]
	private bool _isTreeVisible;

	[ObservableProperty]
	private bool _isDark;

	public TitleBarViewModel(IAppearanceService appearance, IClientSettingsStore settings, IShellCommands commands, IUiDispatcher ui, string buildNumber, bool isBuildOutput)
	{
		_appearance = appearance;
		_settings = settings;
		_commands = commands;
		_ui = ui;
		BuildNumber = buildNumber;
		IsBuildOutput = isBuildOutput;
		_isTreeVisible = !settings.Get<ShellLayoutSettings>().TreeCollapsed;
		_isDark = appearance.IsDark;
		appearance.Changed += (_, _) => _ui.Post(() => IsDark = _appearance.IsDark);
	}

	/// <summary>Build time of this app as <c>yyyyMM.dd.HHmm</c>; empty when it cannot be read.</summary>
	public string BuildNumber { get; }

	/// <summary>The app runs from <c>bin\Debug</c> or <c>bin\Release</c>, so it is never updated on restart.</summary>
	public bool IsBuildOutput { get; }

	public string BuildOutputWarning => Constants.TitleBar.BuildOutputWarning;

	/// <summary>The left panel was shown or hidden (the window lays itself out).</summary>
	public event EventHandler? TreeVisibilityChanged;

	[RelayCommand]
	private void ToggleTree() => SetTreeVisible(!IsTreeVisible);

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
