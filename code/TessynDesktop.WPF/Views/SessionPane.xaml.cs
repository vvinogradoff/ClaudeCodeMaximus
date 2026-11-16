using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The session pane: header, transcript (WebView2), find bar and input box.</summary>
/// <remarks>Created by Claude</remarks>
public partial class SessionPane : UserControl
{
	private SessionPaneViewModel? _subscribed;
	private double _sideWidth = Constants.Limits.DefaultSidePaneWidth;
	private readonly FileDropReader _fileDrops = new();
	private bool _caretIsOurs;

	public SessionPane()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
		Loaded += OnLoaded;
		DataObject.AddPastingHandler(InputBox, OnInputPasting);
		SuggestionsPopup.CustomPopupPlacementCallback = PlaceSuggestionsAt;
		Suggestions.ItemChosen += OnSuggestionChosen;
	}

	// The Session / Focus switch (FR.23.12) also reacts to Checked, besides the click Command: a radio chosen through UI Automation (SelectionItem.Select), which is how
	// assistive technology and the E2E tests choose it, or by the keyboard, is checked without a click, so the Command alone never ran and the view did not switch.
	// The guards make the second execution after a click a no-op.
	private void OnSessionViewChecked(object sender, RoutedEventArgs e)
	{
		if (DataContext is SessionPaneViewModel { IsFocusSelected: true } viewModel)
			viewModel.SelectSessionViewCommand.Execute(null);
	}

	private void OnFocusViewChecked(object sender, RoutedEventArgs e)
	{
		if (DataContext is SessionPaneViewModel { IsFocusSelected: false } viewModel)
			viewModel.SelectFocusViewCommand.Execute(null);
	}

	// W8: a right-click on Send opens the "Send later" dialog (FR.14.4); the same command is bound to a key and listed in the key bindings.
	private void OnSendRightClick(object sender, MouseButtonEventArgs e)
	{
		if (DataContext is SessionPaneViewModel viewModel && viewModel.SendLaterCommand.CanExecute(null))
		{
			e.Handled = true;
			_ = viewModel.SendLaterCommand.ExecuteAsync(null);
		}
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		Loaded -= OnLoaded;
		var surface = ((App)Application.Current).Services.GetRequiredService<WebView2TranscriptSurface>();
		surface.BrowserLost += (_, _) => Dispatcher.BeginInvoke(() => _ = RecreateAsync(surface));
		TryTestDrop();
		await AttachAsync(surface, Transcript);
	}

	private async Task AttachAsync(WebView2TranscriptSurface surface, Microsoft.Web.WebView2.Wpf.WebView2 control)
	{
		var logger = ((App)Application.Current).Services.GetRequiredService<ILogger<SessionPane>>();
		try
		{
			await surface.AttachAsync(control);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "The transcript WebView2 failed to initialize");
		}
	}

	/// <summary>The WebView2 browser process died: replace the control; the surface asks the view model to replay the current session.</summary>
	private async Task RecreateAsync(WebView2TranscriptSurface surface)
	{
		TranscriptHost.Children.Remove(Transcript);
		Transcript.Dispose();
		Transcript = new Microsoft.Web.WebView2.Wpf.WebView2();
		TranscriptHost.Children.Add(Transcript);
		await AttachAsync(surface, Transcript);
	}

	private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		if (_subscribed != null)
		{
			_subscribed.FindFocusRequested -= OnFindFocusRequested;
			_subscribed.InputCaretRequested -= OnInputCaretRequested;
			_subscribed.PropertyChanged -= OnViewModelPropertyChanged;
		}

		_subscribed = e.NewValue as SessionPaneViewModel;
		if (_subscribed != null)
		{
			_subscribed.FindFocusRequested += OnFindFocusRequested;
			_subscribed.InputCaretRequested += OnInputCaretRequested;
			_subscribed.PropertyChanged += OnViewModelPropertyChanged;
		}

		ApplySidePane();
		ApplyInputArea();
	}

	// ---- the collapsible input area (FR.12.29) ----

	/// <summary>The most the input area grows to by itself (the text box and the command bar); a height the user dragged to lifts it.</summary>
	private const double NaturalInputMaxHeight = 260;

	private bool _dragExpanding;

	/// <summary>
	/// Puts the input row in the state the view model asks for: Auto (the content's height) when collapsed or never resized, the remembered height otherwise.
	/// While a drag out of the collapsed state expands the area the splitter sets the height itself, so only the sizing mode changes.
	/// </summary>
	private void ApplyInputArea()
	{
		if (_subscribed == null)
			return;

		if (_dragExpanding)
		{
			SetInputSizing(userSized: true);
			return;
		}

		if (!_subscribed.IsInputCollapsed && _subscribed.ExpandedInputHeight is > 0 and var height)
		{
			InputRow.Height = new GridLength(height);
			SetInputSizing(userSized: true);
		}
		else
		{
			InputRow.Height = GridLength.Auto;
			SetInputSizing(userSized: false);
		}
	}

	/// <summary>
	/// A user-sized row (the splitter was dragged): the cap on the natural height is lifted, so the text box fills the row (the scroll viewer around the content gives it the row's
	/// height) and the content scrolls when the row is smaller than it. A natural row (Auto): the content is as high as it is, capped at <see cref="NaturalInputMaxHeight"/>.
	/// </summary>
	private void SetInputSizing(bool userSized) =>
		InputBody.MaxHeight = userSized ? double.PositiveInfinity : NaturalInputMaxHeight;

	/// <summary>A double click on the splitter collapses or expands the input area; the first press of it only starts a drag.</summary>
	private void OnInputSplitterMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount != 2 || _subscribed == null)
			return;

		_subscribed.ToggleInputAreaCommand.Execute(null);
		e.Handled = true;
	}

	private void OnInputSplitterKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key is not (Key.Enter or Key.Space) || _subscribed == null)
			return;

		_subscribed.ToggleInputAreaCommand.Execute(null);
		e.Handled = true;
	}

	/// <summary>A drag out of the collapsed state expands the area first, then goes on resizing it: the splitter sets the height, the area only changes mode.</summary>
	private void OnInputSplitterDragDelta(object sender, DragDeltaEventArgs e)
	{
		if (_subscribed is not { IsInputCollapsed: true } || Math.Abs(e.VerticalChange) <= 0)
			return;

		_dragExpanding = true;
		try
		{
			_subscribed.IsInputCollapsed = false;
		}
		finally
		{
			_dragExpanding = false;
		}
	}

	/// <summary>The drag starts: from here the row is sized by the user, so the content must fill it (and scroll when it is small).</summary>
	private void OnInputSplitterDragStarted(object sender, DragStartedEventArgs e)
	{
		if (_subscribed is { IsInputCollapsed: false })
			SetInputSizing(userSized: true);
	}

	/// <summary>The row now has the height the user dragged it to: remembered for when the area expands again. A drag that changed nothing leaves the natural height.</summary>
	private void OnInputSplitterDragCompleted(object sender, DragCompletedEventArgs e)
	{
		if (_subscribed is not { IsInputCollapsed: false })
			return;

		if (InputRow.Height.IsAuto || InputRow.ActualHeight <= 0)
		{
			SetInputSizing(userSized: false);
			return;
		}

		_subscribed.ExpandedInputHeight = InputRow.ActualHeight;
		SetInputSizing(userSized: true);
	}

	private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SessionPaneViewModel.IsSidePaneVisible))
			ApplySidePane();

		if (e.PropertyName == nameof(SessionPaneViewModel.IsInputCollapsed))
			ApplyInputArea();

		if (e.PropertyName == nameof(SessionPaneViewModel.IsLoading))
			TryTestDrop();
	}

	/// <summary>Opens or closes the side column; the width the user dragged it to is kept while the pane is hidden.</summary>
	private void ApplySidePane()
	{
		var visible = _subscribed?.IsSidePaneVisible == true;
		if (visible == (SidePane.Visibility == Visibility.Visible))
			return;

		SidePane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
		SideSplitter.Visibility = SidePane.Visibility;
		if (visible)
		{
			SideColumn.MinWidth = Constants.Limits.MinSidePaneWidth;
			SideColumn.Width = new GridLength(_sideWidth);
		}
		else
		{
			if (SideColumn.ActualWidth > 0)
				_sideWidth = SideColumn.ActualWidth;
			SideColumn.MinWidth = 0;
			SideColumn.Width = new GridLength(0);
		}
	}

	private void OnFindFocusRequested(object? sender, EventArgs e)
	{
		Dispatcher.BeginInvoke(() =>
		{
			FindBox.Focus();
			FindBox.SelectAll();
		}, System.Windows.Threading.DispatcherPriority.Input);
	}

	private void OnFindKeyDown(object sender, KeyEventArgs e)
	{
		if (_subscribed == null)
			return;

		switch (e.Key)
		{
			case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
				_subscribed.FindPreviousCommand.Execute(null);
				e.Handled = true;
				break;
			case Key.Enter:
				_ = _subscribed.FindCommand.ExecuteAsync(null);
				e.Handled = true;
				break;
			case Key.Escape:
				_subscribed.CloseFindCommand.Execute(null);
				e.Handled = true;
				break;
		}
	}

	/// <summary>Only a real file list (<see cref="DataFormats.FileDrop"/>) is taken over; any other drag keeps the text box's own behaviour.</summary>
	private void OnInputDragOver(object sender, DragEventArgs e)
	{
		if (_subscribed == null || !e.Data.GetDataPresent(DataFormats.FileDrop))   // presence only: the list is read on Drop, not on every DragOver
			return;

		e.Effects = DragDropEffects.Copy;
		e.Handled = true;
	}

	private void OnInputDrop(object sender, DragEventArgs e)
	{
		var paths = _fileDrops.Read(e.Data);
		if (_subscribed == null || paths.Count == 0)
			return;

		e.Handled = true;
		_ = _subscribed.HandleFilesAsync(paths, InputBox.SelectionStart, InputBox.SelectionLength);
	}

	/// <summary>Ctrl+V of files copied in Explorer behaves like a drop; text on the clipboard pastes as usual.</summary>
	private void OnInputPasting(object sender, DataObjectPastingEventArgs e)
	{
		if (_subscribed == null || e.DataObject.GetDataPresent(DataFormats.UnicodeText) || e.DataObject.GetDataPresent(DataFormats.Text))
			return;

		var paths = _fileDrops.Read(e.DataObject);
		if (paths.Count == 0)
			return;

		e.CancelCommand();
		_ = _subscribed.HandleFilesAsync(paths, InputBox.SelectionStart, InputBox.SelectionLength);
	}

	private void OnInputCaretRequested(object? sender, int caret)
	{
		// The caret move is the pane's own (a drop, an accepted suggestion, a draft arriving): it is no typing and must not open the popup.
		_caretIsOurs = true;
		try
		{
			InputBox.CaretIndex = Math.Min(caret, InputBox.Text.Length);
		}
		finally
		{
			_caretIsOurs = false;
		}

		if (IsKeyboardFocusWithin || InputBox.IsKeyboardFocused)   // never pull focus away from another control or window
			InputBox.Focus();
	}

	// ---- autocomplete (W1b) ----

	/// <summary>The text or the caret of the input changed: typing and caret moves of the user ask for suggestions, the pane's own changes close the popup.</summary>
	private void OnInputEdited(object sender, RoutedEventArgs e)
	{
		if (_subscribed == null)
			return;

		if (_caretIsOurs || _subscribed.IsProgrammaticInput)
		{
			_subscribed.Autocomplete.Close();
			return;
		}

		_subscribed.Autocomplete.Update(InputBox.Text, InputBox.CaretIndex);
		PlaceSuggestions();
	}

	private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e) => _subscribed?.Autocomplete.Close();

	private Services.IAppearanceService? _appearance;

	/// <summary>Ctrl+wheel over the prompt box changes its font size by a point (as in the Avalonia client).</summary>
	private void OnInputWheel(object sender, MouseWheelEventArgs e)
	{
		if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || e.Delta == 0)
			return;

		(_appearance ??= new ShellServiceProvider().Get<Services.IAppearanceService>())?.AdjustFont(Services.FontTarget.Input, e.Delta > 0 ? 1 : -1);
		e.Handled = true;
	}

	/// <summary>Up, Down, Tab, Enter and Esc drive the popup while it is open; Ctrl+Enter still sends, every other key types as usual.</summary>
	private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
	{
		var autocomplete = _subscribed?.Autocomplete;
		if (autocomplete is not { IsOpen: true })
			return;

		switch (e.Key)
		{
			case Key.Down:
				autocomplete.Move(1);
				e.Handled = true;
				break;
			case Key.Up:
				autocomplete.Move(-1);
				e.Handled = true;
				break;
			case Key.Escape:
				autocomplete.Close();
				e.Handled = true;
				break;
			case Key.Tab when !Keyboard.Modifiers.HasFlag(ModifierKeys.Control):
			case Key.Enter when !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
				// M5: the key is taken only when a suggestion was accepted; with stale items (text typed since the answer) it types as usual.
				e.Handled = AcceptSuggestion(-1);
				break;
		}
	}

	private void OnSuggestionChosen(object? sender, int index)
	{
		AcceptSuggestion(index);
		InputBox.Focus();
	}

	/// <summary>Accepts a suggestion; false when there was nothing to accept (no popup content, or the text moved on since the answer).</summary>
	private bool AcceptSuggestion(int index)
	{
		var insertion = _subscribed?.AcceptSuggestion(index);
		if (insertion is { ContinuesPath: true })
		{
			// A directory was accepted: the path goes on, so the next level is offered.
			Dispatcher.BeginInvoke(() => _subscribed?.Autocomplete.Update(InputBox.Text, InputBox.CaretIndex), System.Windows.Threading.DispatcherPriority.Input);
		}

		return insertion != null;
	}

	/// <summary>The popup opens above the caret (or below it when there is no room above): it never covers the line being typed.</summary>
	private CustomPopupPlacement[] PlaceSuggestionsAt(Size popupSize, Size targetSize, Point offset)
	{
		var caret = InputBox.GetRectFromCharacterIndex(Math.Min(InputBox.CaretIndex, InputBox.Text.Length));
		var left = caret.IsEmpty ? 0 : Math.Clamp(caret.Left, 0, Math.Max(0, targetSize.Width - popupSize.Width));
		var top = caret.IsEmpty ? 0 : Math.Clamp(caret.Top, 0, targetSize.Height);
		var bottom = caret.IsEmpty ? targetSize.Height : Math.Clamp(caret.Bottom, 0, targetSize.Height);
		return
		[
			new CustomPopupPlacement(new Point(left, top - popupSize.Height), PopupPrimaryAxis.Horizontal),
			new CustomPopupPlacement(new Point(left, bottom), PopupPrimaryAxis.Horizontal),
		];
	}

	/// <summary>Asks the popup to place itself again (the caret moved while it is open).</summary>
	private void PlaceSuggestions()
	{
		if (SuggestionsPopup.IsOpen)
			SuggestionsPopup.VerticalOffset = SuggestionsPopup.VerticalOffset == 0 ? 0.01 : 0;
	}

	/// <summary>The <c>--test-drop</c> seam (see <see cref="SessionPaneViewModel.TestDropPaths"/>): hands the paths over once, when the first session is shown.</summary>
	private void TryTestDrop()
	{
		if (_subscribed is not { HasSession: true, IsLoading: false })
			return;

		var paths = _subscribed.TakeTestDropPaths();
		if (paths.Count > 0)
			_ = _subscribed.HandleFilesAsync(paths, InputBox.SelectionStart, InputBox.SelectionLength);
	}
}
