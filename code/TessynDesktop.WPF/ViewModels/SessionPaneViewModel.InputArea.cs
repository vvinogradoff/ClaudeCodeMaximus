using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.State;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// The Session/Focus switch (FR.23.12) and the collapsible input area (FR.12.29) of the session pane.
/// <para>
/// The selected view is kept per session in memory, never persisted; a session that was not opened yet starts on Session. The input area is expanded in the
/// Session view and collapsed in the Focus view, never persisted and never carried over: opening a different session, or switching the view, sets the state
/// to the default of the view entered. The command bar's visibility (FR.12.11) is a separate switch the collapse leaves alone.
/// </para>
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class SessionPaneViewModel
{
	private readonly Dictionary<string, bool> _focusSelection = new(StringComparer.Ordinal);
	private bool _applyingView;

	/// <summary>The Focus view is selected: it replaces the transcript (FR.23.12).</summary>
	[ObservableProperty]
	private bool _isFocusSelected;

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

	public bool IsSessionSelected => !IsFocusSelected;

	/// <summary>The Focus badge shows only while the Session view is selected and there is something open (FR.23.13).</summary>
	public bool IsFocusBadgeVisible => !IsFocusSelected && FocusBadge > 0;

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

	partial void OnIsFocusSelectedChanged(bool value)
	{
		OnPropertyChanged(nameof(IsSessionSelected));
		OnPropertyChanged(nameof(IsFocusBadgeVisible));
		if (_applyingView)
			return;

		if (NodeId != null)
			_focusSelection[NodeId] = value;

		IsInputCollapsed = value;   // entering a view applies its default (FR.12.29)
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

	/// <summary>A different session opened: its remembered view (Session for a new one) is selected and the input area takes that view's default.</summary>
	private void ResetViewForOpen(string nodeId)
	{
		_applyingView = true;
		try
		{
			IsFocusSelected = _focusSelection.GetValueOrDefault(nodeId);
		}
		finally
		{
			_applyingView = false;
		}

		IsInputCollapsed = IsFocusSelected;
	}

	/// <summary>The Session view was chosen (FR.23.12).</summary>
	[RelayCommand]
	private void SelectSessionView() => IsFocusSelected = false;

	/// <summary>The Focus view was chosen (FR.23.12).</summary>
	[RelayCommand]
	private void SelectFocusView() => IsFocusSelected = true;

	/// <summary>Collapses an expanded input area and expands a collapsed one (a double click on the splitter above it, FR.12.29).</summary>
	[RelayCommand]
	private void ToggleInputArea() => IsInputCollapsed = !IsInputCollapsed;

	/// <summary>Shows or hides the command bar (the settings toggle, FR.12.1); only offered while the input area is expanded.</summary>
	[RelayCommand]
	private void ToggleCommandBar() => IsCommandBarVisible = !IsCommandBarVisible;
}
