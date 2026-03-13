using System;
using System.Collections.Specialized;
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

	public SessionView()
	{
		InitializeComponent();

		// Ctrl+Enter / plain Enter handling + autocomplete keyboard
		InputBox.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);

		// Text/caret change → trigger detection
		InputBox.PropertyChanged += OnInputBoxPropertyChanged;

		// Ctrl+scroll changes font size; tunnel so we intercept before the scroller scrolls
		MessageScroller.AddHandler(InputElement.PointerWheelChangedEvent, OnScrollerWheel, RoutingStrategies.Tunnel);
		InputBox.AddHandler(InputElement.PointerWheelChangedEvent, OnInputBoxWheel, RoutingStrategies.Tunnel);
	}

	protected override void OnDataContextChanged(EventArgs e)
	{
		base.OnDataContextChanged(e);

		// Unsubscribe from the previous session's messages (fixes cross-session scroll leak)
		if (_subscribedVm != null)
			_subscribedVm.Messages.CollectionChanged -= OnMessagesChanged;

		_subscribedVm = DataContext as SessionViewModel;

		if (_subscribedVm != null)
		{
			_subscribedVm.Messages.CollectionChanged += OnMessagesChanged;
			// Scroll to bottom once when switching to a session, not on every new message
			Dispatcher.UIThread.Post(() => MessageScroller.ScrollToEnd(), DispatcherPriority.Background);
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

		UpdateAutocompleteTrigger();
	}

	private void UpdateAutocompleteTrigger()
	{
		if (DataContext is not SessionViewModel vm) return;

		var text = InputBox.Text ?? string.Empty;
		var caret = InputBox.CaretIndex;
		var trigger = _triggerParser.Parse(text, caret);

		vm.AutocompleteVm.UpdateSuggestions(vm.WorkingDirectory, trigger);
	}

	// No-op: user controls their own scroll position. Subscription kept to track collection changes if needed.
	private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e) { }

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
