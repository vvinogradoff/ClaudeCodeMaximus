using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The Focus button (FR.6.16) and the collapsible input area (FR.12.29) of the session pane.
/// <para>
/// The Focus button brings the Focus panel forward through <see cref="IShellLayout"/>. The input area belongs to the Session view only (FR.6.17): it is expanded
/// when a different session opens, never persisted and never carried over. The command bar's visibility (FR.12.11) is a separate switch the collapse leaves alone.
/// </para>
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private IShellLayout? _shellLayout;

	/// <summary>The docked shell the Focus button brings the Focus panel forward in (FR.6.16); null in tests and tools without a shell. Set once, when the pane is created.</summary>
	public IShellLayout? ShellLayout
	{
		get => _shellLayout;
		init => _shellLayout = value;
	}

	/// <summary>The tooltips of the Focus and Session buttons with the keys in effect (FR.6.23.8); without a registry (tests, tools) they carry no key.</summary>
	public ShellButtonHints Hints { get; init; } = new();

	/// <summary>The input area is collapsed (FR.12.29): the command bar only while it is visible, otherwise one line of the input text.</summary>
	[ObservableProperty]
	private bool _isInputCollapsed;

	/// <summary>The command bar (model, effort, profile) under the input box is shown (FR.12.11); remembered across restarts, one setting for the client.</summary>
	[ObservableProperty]
	private bool _isCommandBarVisible = true;

	private double? _expandedInputHeight;

	/// <summary>
	/// The height the user dragged the expanded input area to; null while it has its natural height. Kept across restarts in the client state (FR.12.36):
	/// read at start, written when it changes. A value that is not positive counts as none.
	/// </summary>
	public double? ExpandedInputHeight
	{
		get => _expandedInputHeight;
		set
		{
			var height = value is > 0 ? value : null;
			_expandedInputHeight = height;
			if (_stateStore != null && _stateStore.State.InputAreaHeight != height)
				_stateStore.Update(s => s.InputAreaHeight = height);
		}
	}

	/// <summary>The Focus button's badge shows whenever there is something open (FR.23.13, FR.6.16).</summary>
	public bool IsFocusBadgeVisible => FocusBadge > 0;

	public bool IsInputExpanded => !IsInputCollapsed;

	/// <summary>The input box shows when the area is expanded, or collapsed with the command bar hidden (then as one line).</summary>
	public bool ShowInputBox => !IsInputCollapsed || !IsCommandBarVisible;

	/// <summary>The instruction toolbar, the file-mode toggle and the settings toggle belong to the expanded area only.</summary>
	public bool ShowExpandedExtras => !IsInputCollapsed;

	/// <summary>Stop shows while a turn runs (Send stays beside it when expanded).</summary>
	public bool ShowStopButton => IsRunning;

	/// <summary>Send shows when expanded, and when collapsed unless Stop does: the collapsed side column has exactly one button.</summary>
	public bool ShowSendButton => !IsInputCollapsed || !IsRunning;

	private void InitializeInputArea(IClientStateStore? stateStore)
	{
		_isCommandBarVisible = stateStore?.State.CommandBarVisible ?? true;
		_expandedInputHeight = stateStore?.State.InputAreaHeight is > 0 and var height ? height : null;
	}

	partial void OnIsInputCollapsedChanged(bool value) => RaiseInputAreaChanged();

	partial void OnIsCommandBarVisibleChanged(bool value)
	{
		if (_stateStore != null && _stateStore.State.CommandBarVisible != value)
			_stateStore.Update(s => s.CommandBarVisible = value);

		RaiseInputAreaChanged();
	}

	private void OnRunningChangedForInput()
	{
		OnPropertyChanged(nameof(ShowStopButton));
		OnPropertyChanged(nameof(ShowSendButton));
	}

	private void RaiseInputAreaChanged()
	{
		OnPropertyChanged(nameof(IsInputExpanded));
		OnPropertyChanged(nameof(ShowInputBox));
		OnPropertyChanged(nameof(ShowExpandedExtras));
		OnPropertyChanged(nameof(ShowSendButton));
		OnPropertyChanged(nameof(ShowStopButton));
	}

	/// <summary>A different session opened: the input area takes its default, expanded (FR.12.29).</summary>
	private void ResetViewForOpen() => IsInputCollapsed = false;

	/// <summary>The Focus button was pressed (FR.6.16): brings the Focus panel forward; it never moves the keyboard focus (FR.6.20).</summary>
	[RelayCommand]
	private void BringFocusPanelForward() => _shellLayout?.BringForward(Constants.DockedShell.FocusPanelId);

	/// <summary>The Session button of the Focus header was pressed (FR.6.23.7): brings the Session view forward; it never moves the keyboard focus (FR.6.20).</summary>
	[RelayCommand]
	private void BringSessionForward() => _shellLayout?.BringForward(Constants.DockedShell.SessionPanelId);

	/// <summary>Collapses an expanded input area and expands a collapsed one (a double click on the splitter above it, FR.12.29).</summary>
	[RelayCommand]
	private void ToggleInputArea() => IsInputCollapsed = !IsInputCollapsed;

	/// <summary>Shows or hides the command bar (the settings toggle, FR.12.1); only offered while the input area is expanded.</summary>
	[RelayCommand]
	private void ToggleCommandBar() => IsCommandBarVisible = !IsCommandBarVisible;
}
