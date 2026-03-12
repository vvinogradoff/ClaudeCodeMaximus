using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClaudeMaximus.ViewModels;
using Serilog;

namespace ClaudeMaximus.Views;

/// <remarks>Created by Claude</remarks>
public partial class SessionView : UserControl
{
	private static readonly ILogger _log = Log.ForContext<SessionView>();
	private SessionViewModel? _subscribedVm;

	public SessionView()
	{
		InitializeComponent();

		// Ctrl+Enter / plain Enter handling
		InputBox.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);

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
		if (e.Key != Key.Enter) return;

		if (e.KeyModifiers == KeyModifiers.Control)
		{
			_log.Debug("Ctrl+Enter pressed — sending message");
			e.Handled = true;
			if (DataContext is SessionViewModel vm)
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
