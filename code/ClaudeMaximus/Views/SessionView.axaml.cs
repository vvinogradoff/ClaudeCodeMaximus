using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using ClaudeMaximus.ViewModels;
using Serilog;

namespace ClaudeMaximus.Views;

/// <remarks>Created by Claude</remarks>
public partial class SessionView : UserControl
{
	private static readonly ILogger _log = Log.ForContext<SessionView>();
	private SessionViewModel? _subscribedVm;
	private readonly AutocompleteTriggerParser _triggerParser = new();
	private DispatcherTimer? _autocompleteDebounce;
	private bool _isAtBottom = true;
	private MessageEntryViewModel? _subscribedProgressMsg;

	/// <summary>Threshold in pixels — if within this distance of the bottom, consider "at bottom".</summary>
	private const double AtBottomThreshold = 30;

	public SessionView()
	{
		InitializeComponent();

		// Ctrl+Enter / plain Enter handling + autocomplete keyboard
		InputBox.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);

		// Text/caret change → trigger detection
		InputBox.PropertyChanged += OnInputBoxPropertyChanged;

		// Output search box keyboard (Enter=next, Ctrl+Enter=prev, Escape=dismiss)
		OutputSearchBox.AddHandler(KeyDownEvent, OnSearchBoxKeyDown, RoutingStrategies.Tunnel);

		// Overlay buttons
		SearchPrevBtn.Click  += (_, _) => NavigateSearch(forward: false);
		SearchNextBtn.Click  += (_, _) => NavigateSearch(forward: true);
		SearchCloseBtn.Click += (_, _) => DismissSearch();

		// Track whether user is at the bottom of the scroller for auto-scroll
		MessageScroller.ScrollChanged += OnScrollChanged;

		// Ctrl+scroll changes font size; tunnel so we intercept before the scroller scrolls
		MessageScroller.AddHandler(InputElement.PointerWheelChangedEvent, OnScrollerWheel, RoutingStrategies.Tunnel);
		InputBox.AddHandler(InputElement.PointerWheelChangedEvent, OnInputBoxWheel, RoutingStrategies.Tunnel);
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		base.OnDataContextChanged(e);

		// Save scroll position of previous session
		if (_subscribedVm != null)
		{
			_subscribedVm.ScrollOffset = MessageScroller.Offset.Y;
			_subscribedVm.Messages.CollectionChanged -= OnMessagesChanged;
			UnsubscribeProgressMessage();
		}

		_subscribedVm = DataContext as SessionViewModel;

		if (_subscribedVm != null)
		{
			_subscribedVm.Messages.CollectionChanged += OnMessagesChanged;
			// Restore persisted scroll position (or bottom for new sessions)
			var savedOffset = _subscribedVm.ScrollOffset;
			Dispatcher.UIThread.Post(() =>
			{
				if (savedOffset > 0)
					MessageScroller.Offset = new Avalonia.Vector(0, savedOffset);
				else
					MessageScroller.ScrollToEnd();
			}, DispatcherPriority.Background);
		}
	}

	private void OnInputKeyDown(object? sender, KeyEventArgs e)
	{
		if (DataContext is not SessionViewModel vm) return;
		var acVm = vm.AutocompleteVm;

		// Autocomplete keyboard handling when popup is open
		if (acVm.IsOpen)
		{
			switch (e.Key)
			{
				case Key.Up:
					acVm.MoveSelection(-1);
					e.Handled = true;
					return;

				case Key.Down:
					acVm.MoveSelection(1);
					e.Handled = true;
					return;

				case Key.Tab:
					AcceptAutocompleteSuggestion(vm);
					e.Handled = true;
					return;

				case Key.Escape:
					acVm.Dismiss();
					e.Handled = true;
					return;

				case Key.Enter when e.KeyModifiers == KeyModifiers.None:
					AcceptAutocompleteSuggestion(vm);
					e.Handled = true;
					return;
			}
		}

		if (e.Key != Key.Enter) return;

		if (e.KeyModifiers == KeyModifiers.Control)
		{
			_log.Debug("Ctrl+Enter pressed — sending message");
			e.Handled = true;
			vm.SendCommand.Execute(default)
				.Subscribe(new System.Reactive.AnonymousObserver<System.Reactive.Unit>(_ => { }, _ => { }, () => { }));
		}
		else if (e.KeyModifiers == KeyModifiers.None && sender is TextBox tb)
		{
			// On Windows, Avalonia inserts \r\n for Enter; intercept and insert \n only
			e.Handled = true;
			var pos  = tb.CaretIndex;
			var text = tb.Text ?? string.Empty;
			tb.Text       = text.Insert(pos, "\n");
			tb.CaretIndex = pos + 1;
		}
	}

	private void AcceptAutocompleteSuggestion(SessionViewModel vm)
	{
		var text = InputBox.Text ?? string.Empty;
		var caret = InputBox.CaretIndex;
		var trigger = _triggerParser.Parse(text, caret);

		var suggestion = vm.AutocompleteVm.AcceptSelection();
		if (suggestion == null || trigger.Mode == AutocompleteMode.None) return;

		// Replace trigger text (including # or ##) with the insert text
		var before = text.Substring(0, trigger.TriggerStartIndex);
		var after = text.Substring(trigger.TriggerStartIndex + trigger.TriggerLength);
		var newText = before + suggestion.InsertText + after;

		InputBox.Text = newText;
		InputBox.CaretIndex = before.Length + suggestion.InsertText.Length;
	}

	private void OnInputBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property.Name is not (nameof(TextBox.Text) or nameof(TextBox.CaretIndex)))
			return;

		_autocompleteDebounce?.Stop();
		_autocompleteDebounce = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(Constants.AutocompleteDebounceMilliseconds)
		};
		_autocompleteDebounce.Tick += (_, _) =>
		{
			_autocompleteDebounce?.Stop();
			_autocompleteDebounce = null;
			UpdateAutocompleteTrigger();
		};
		_autocompleteDebounce.Start();
	}

	private void UpdateAutocompleteTrigger()
	{
		if (DataContext is not SessionViewModel vm) return;

		var text = InputBox.Text ?? string.Empty;
		var caret = InputBox.CaretIndex;
		var trigger = _triggerParser.Parse(text, caret);

		vm.AutocompleteVm.UpdateSuggestions(vm.WorkingDirectory, trigger);
	}

	private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
	{
		var extent = MessageScroller.Extent.Height;
		var viewport = MessageScroller.Viewport.Height;
		var offset = MessageScroller.Offset.Y;
		_isAtBottom = extent - viewport - offset <= AtBottomThreshold;
	}

	private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		// Unsubscribe from previously tracked progress message
		UnsubscribeProgressMessage();

		if (e.Action == NotifyCollectionChangedAction.Add && _subscribedVm != null)
		{
			// Subscribe to content changes on the last message (handles streaming/progress updates)
			var last = _subscribedVm.Messages.Count > 0 ? _subscribedVm.Messages[^1] : null;
			if (last != null)
			{
				_subscribedProgressMsg = last;
				last.PropertyChanged += OnLastMessagePropertyChanged;
			}
		}

		if (_isAtBottom)
			ScrollToEndDeferred();
	}

	private void OnLastMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(MessageEntryViewModel.Content) && _isAtBottom)
			ScrollToEndDeferred();
	}

	private void UnsubscribeProgressMessage()
	{
		if (_subscribedProgressMsg != null)
		{
			_subscribedProgressMsg.PropertyChanged -= OnLastMessagePropertyChanged;
			_subscribedProgressMsg = null;
		}
	}

	private void ScrollToEndDeferred()
	{
		Dispatcher.UIThread.Post(() => MessageScroller.ScrollToEnd(), DispatcherPriority.Background);
	}

	private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			DismissSearch();
			e.Handled = true;
			return;
		}

		if (e.Key != Key.Enter)
			return;

		e.Handled = true;

		if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
			NavigateSearch(forward: false);
		else
			NavigateSearch(forward: true);
	}

	private void NavigateSearch(bool forward)
	{
		if (DataContext is not SessionViewModel vm)
			return;

		var search = vm.OutputSearchVm;
		int msgIndex;

		if (!search.IsActive)
		{
			// First search or re-search after dismiss
			msgIndex = search.Search(OutputSearchBox.Text ?? string.Empty);
		}
		else
		{
			msgIndex = forward ? search.NextMatch() : search.PreviousMatch();
		}

		if (msgIndex >= 0)
			ScrollToMessageIndex(msgIndex);
	}

	private void DismissSearch()
	{
		if (DataContext is SessionViewModel vm)
			vm.OutputSearchVm.Dismiss();
	}

	private void ScrollToMessageIndex(int index)
	{
		// Defer so layout has a chance to update
		Dispatcher.UIThread.Post(() =>
		{
			var container = MessageList.ContainerFromIndex(index);
			if (container is Control ctrl)
				ctrl.BringIntoView();
		}, DispatcherPriority.Background);
	}

	private void OnScrollerWheel(object? sender, PointerWheelEventArgs e)
	{
		if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
		if (DataContext is not SessionViewModel vm) return;

		var delta = e.Delta.Y > 0 ? 1.0 : -1.0;
		var msgVm = FindMessageViewModel(e.Source);

		if (msgVm?.IsAssistant == true)
		{
			if (vm.IsMarkdownMode)
				vm.AssistantMarkdownFontSize = Math.Clamp(vm.AssistantMarkdownFontSize + delta, 8, 32);
			else
				vm.AssistantFontSize = Math.Clamp(vm.AssistantFontSize + delta, 8, 32);
		}
		else if (msgVm?.IsUser == true)
			vm.UserFontSize = Math.Clamp(vm.UserFontSize + delta, 8, 32);

		e.Handled = true;
	}

	private void OnInputBoxWheel(object? sender, PointerWheelEventArgs e)
	{
		if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
		if (DataContext is not SessionViewModel vm) return;

		var delta = e.Delta.Y > 0 ? 1.0 : -1.0;
		vm.InputFontSize = Math.Clamp(vm.InputFontSize + delta, 8, 32);
		e.Handled = true;
	}

	private static MessageEntryViewModel? FindMessageViewModel(object? source)
	{
		var visual = source as Visual;
		while (visual != null)
		{
			if (visual is StyledElement { DataContext: MessageEntryViewModel vm })
				return vm;
			visual = visual.GetVisualParent();
		}
		return null;
	}
}
