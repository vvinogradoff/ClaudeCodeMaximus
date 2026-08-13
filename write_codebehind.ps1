$path = 'C:\Projects\oDesk\Duxre\Code\ClaudeMaximus\code\ClaudeMaximus\Views\SessionView.axaml.cs'
$src = @"
using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ClaudeMaximus.ViewModels;
using Serilog;

namespace ClaudeMaximus.Views;

/// <remarks>Created by Claude</remarks>
public partial class SessionView : UserControl
{
	private static readonly ILogger _log = Log.ForContext<SessionView>();

	private const double FontSizeMin  = 8.0;
	private const double FontSizeMax  = 32.0;
	private const double FontSizeStep = 1.0;

	public SessionView()
	{
		InitializeComponent();

		InputBox.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
		MessageScroller.AddHandler(PointerWheelChangedEvent, OnMessageScrollerWheel, RoutingStrategies.Tunnel);
		InputBox.AddHandler(PointerWheelChangedEvent, OnInputBoxWheel, RoutingStrategies.Tunnel);
	}

	protected override void OnDataContextChanged(System.EventArgs e)
	{
		base.OnDataContextChanged(e);

		if (DataContext is SessionViewModel vm)
		{
			MessageList.Items.CollectionChanged -= OnMessagesChanged;
			vm.Messages.CollectionChanged += OnMessagesChanged;
		}
	}

	private void OnInputKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key != Key.Enter) return;

		if (e.KeyModifiers == KeyModifiers.Control)
		{
			_log.Debug("Ctrl+Enter pressed - sending message");
			e.Handled = true;
			if (DataContext is SessionViewModel vm)
				vm.SendCommand.Execute(default)
					.Subscribe(new System.Reactive.AnonymousObserver<System.Reactive.Unit>(_ => { }, _ => { }, () => { }));
		}
		else if (e.KeyModifiers == KeyModifiers.None && sender is TextBox tb)
		{
			e.Handled = true;
			var pos  = tb.CaretIndex;
			var text = tb.Text ?? string.Empty;
			tb.Text       = text.Insert(pos, "\n");
			tb.CaretIndex = pos + 1;
		}
	}

	private void OnMessageScrollerWheel(object? sender, PointerWheelEventArgs e)
	{
		if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
		if (DataContext is not SessionViewModel vm) return;

		e.Handled = true;
		var delta = e.Delta.Y > 0 ? FontSizeStep : -FontSizeStep;

		var visual = e.Source as Avalonia.Visual;
		while (visual != null)
		{
			if (visual is Control { DataContext: MessageEntryViewModel msg })
			{
				if (msg.IsUser)
					vm.UserFontSize = Math.Clamp(vm.UserFontSize + delta, FontSizeMin, FontSizeMax);
				else if (msg.IsAssistant)
					vm.AssistantFontSize = Math.Clamp(vm.AssistantFontSize + delta, FontSizeMin, FontSizeMax);
				return;
			}
			visual = visual.GetVisualParent();
		}
	}

	private void OnInputBoxWheel(object? sender, PointerWheelEventArgs e)
	{
		if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
		if (DataContext is not SessionViewModel vm) return;

		e.Handled = true;
		var delta = e.Delta.Y > 0 ? FontSizeStep : -FontSizeStep;
		vm.InputFontSize = Math.Clamp(vm.InputFontSize + delta, FontSizeMin, FontSizeMax);
	}

	private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.Action == NotifyCollectionChangedAction.Add)
			MessageScroller.ScrollToEnd();
	}
}
"@
[System.IO.File]::WriteAllText($path, $src, [System.Text.Encoding]::UTF8)
Write-Host "Written: $path"
