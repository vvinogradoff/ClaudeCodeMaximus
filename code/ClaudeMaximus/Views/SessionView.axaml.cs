using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ClaudeMaximus.Models;
using ClaudeMaximus.Services;
using ClaudeMaximus.ViewModels;
using Microsoft.Extensions.DependencyInjection;
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
	private CrossBlockSelectionHandler? _crossBlockSelection;

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

		// Cross-block text selection (works in both text and markdown modes)
		_crossBlockSelection = new CrossBlockSelectionHandler(MessageScroller);

		// Track whether user is at the bottom of the scroller for auto-scroll
		MessageScroller.ScrollChanged += OnScrollChanged;

		// Ctrl+scroll changes font size; tunnel so we intercept before the scroller scrolls
		MessageScroller.AddHandler(InputElement.PointerWheelChangedEvent, OnScrollerWheel, RoutingStrategies.Tunnel);
		InputBox.AddHandler(InputElement.PointerWheelChangedEvent, OnInputBoxWheel, RoutingStrategies.Tunnel);

		// Drag-and-drop for file attachments — accept on both the input area and the conversation area
		InputAreaRoot.AddHandler(DragDrop.DragOverEvent, OnDragOver);
		InputAreaRoot.AddHandler(DragDrop.DropEvent, OnDrop);
		MessageScroller.AddHandler(DragDrop.DragOverEvent, OnDragOver);
		MessageScroller.AddHandler(DragDrop.DropEvent, OnDrop);

		// Clipboard paste interception (image bytes / file URIs in addition to text)
		InputBox.AddHandler(InputElement.KeyDownEvent, OnInputPasteCheck, RoutingStrategies.Tunnel);
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

		var keyService = App.Services.GetRequiredService<IKeyBindingService>();
		if (keyService.Matches(Constants.KeyBindings.Send, e))
		{
			_log.Debug("Send hotkey pressed — sending message");
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
		var searchText = OutputSearchBox.Text ?? string.Empty;
		int msgIndex;

		if (!search.IsActive ||
		    !string.Equals(searchText, search.ActiveSearchTerm, StringComparison.Ordinal))
		{
			// First search, re-search after dismiss, or search text changed
			msgIndex = search.Search(searchText);
		}
		else
		{
			msgIndex = forward ? search.NextMatch() : search.PreviousMatch();
		}

		if (msgIndex >= 0)
			ScrollToMatchInMessage(msgIndex, searchText);
	}

	private void DismissSearch()
	{
		if (DataContext is SessionViewModel vm)
			vm.OutputSearchVm.Dismiss();
	}

	private void ScrollToMatchInMessage(int messageIndex, string searchTerm)
	{
		// Defer so layout has a chance to update after highlight rebuilds
		Dispatcher.UIThread.Post(() =>
		{
			var container = MessageList.ContainerFromIndex(messageIndex);
			if (container is not Control ctrl) return;

			// First, ensure the container is measured/arranged
			ctrl.BringIntoView();

			// Then schedule a second pass to do precise positioning
			Dispatcher.UIThread.Post(() => ScrollToPreciseMatch(ctrl, messageIndex, searchTerm),
				DispatcherPriority.Background);
		}, DispatcherPriority.Background);
	}

	private void ScrollToPreciseMatch(Control container, int messageIndex, string searchTerm)
	{
		if (DataContext is not SessionViewModel vm) return;

		// Get the message content to estimate where the match is within the container
		if (messageIndex < 0 || messageIndex >= vm.Messages.Count) return;
		var msg = vm.Messages[messageIndex];
		var content = msg.Content;
		if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(searchTerm)) return;

		var matchCharIndex = content.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase);
		if (matchCharIndex < 0) return;

		// Get container position relative to the scroll content
		var transform = container.TransformToVisual(MessageScroller);
		if (transform == null) return;

		var containerTopInViewport = transform.Value.Transform(new Point(0, 0)).Y;
		var containerHeight = container.Bounds.Height;
		var viewportHeight = MessageScroller.Viewport.Height;

		// Estimate the vertical position of the match within the container
		// based on character position ratio
		double matchRatio = content.Length > 0 ? (double)matchCharIndex / content.Length : 0;
		double estimatedMatchY = containerTopInViewport + (matchRatio * containerHeight);

		// Target: put the match at 25% from the top of the viewport
		double targetViewportY = viewportHeight * 0.25;
		double scrollDelta = estimatedMatchY - targetViewportY;
		double newOffset = MessageScroller.Offset.Y + scrollDelta;

		// Clamp to valid range
		var maxOffset = MessageScroller.Extent.Height - viewportHeight;
		newOffset = Math.Clamp(newOffset, 0, Math.Max(0, maxOffset));

		MessageScroller.Offset = new Avalonia.Vector(0, newOffset);
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

	// =====================================================================
	// Attachments: file picker, drag-drop, clipboard paste, chip removal
	// =====================================================================

	private async void OnAttachClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not SessionViewModel vm) return;

		var topLevel = TopLevel.GetTopLevel(this);
		if (topLevel == null) return;

		try
		{
			var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
			{
				Title = "Attach files",
				AllowMultiple = true,
			});

			foreach (var file in files)
			{
				var path = file.TryGetLocalPath();
				if (!string.IsNullOrEmpty(path))
					await vm.AddAttachmentFromFileAsync(path);
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "File picker failed");
		}
	}

	private void OnRemoveAttachmentClick(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not SessionViewModel vm) return;
		if (sender is Button btn && btn.Tag is Guid id)
			vm.RemoveAttachment(id);
	}

#pragma warning disable CS0618 // Avalonia's new IDataTransfer API is undocumented; the legacy IDataObject API still works.
	private void OnDragOver(object? sender, DragEventArgs e)
	{
		// Accept files and bitmaps
		if (e.Data.Contains(DataFormats.Files) || e.Data.Contains("image/png") || e.Data.Contains("image/jpeg"))
			e.DragEffects = DragDropEffects.Copy;
		else
			e.DragEffects = DragDropEffects.None;
	}

	private async void OnDrop(object? sender, DragEventArgs e)
	{
		if (DataContext is not SessionViewModel vm) return;

		try
		{
			if (e.Data.Contains(DataFormats.Files))
			{
				var items = e.Data.GetFiles();
				if (items != null)
				{
					foreach (var item in items)
					{
						var path = item.TryGetLocalPath();
						if (!string.IsNullOrEmpty(path) && File.Exists(path))
							await vm.AddAttachmentFromFileAsync(path);
					}
				}
			}
		}
		catch (Exception ex)
		{
			_log.Warning(ex, "Drop handling failed");
		}
	}
#pragma warning restore CS0618

	private async void OnInputPasteCheck(object? sender, KeyEventArgs e)
	{
		// Detect Cmd+V / Ctrl+V
		var isPaste = e.Key == Key.V &&
		              (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control));
		if (!isPaste) return;
		if (DataContext is not SessionViewModel vm) return;

		var topLevel = TopLevel.GetTopLevel(this);
		var clipboard = topLevel?.Clipboard;
		if (clipboard == null) return;

		try
		{
			// Avalonia 11's IAsyncDataTransfer API exposes pasted images as a Bitmap object
			// (decoded from whatever format the platform provided — TIFF on macOS screenshots,
			// PNG from screenshot tools, etc.). We re-encode to PNG since Anthropic's API
			// only accepts PNG/JPEG/GIF/WebP.
			//
			// We use reflection because the API isn't directly exposed in stable Avalonia headers,
			// but the implementation has been stable for several releases.
			var bitmap = await TryGetClipboardBitmapAsync(clipboard);
			if (bitmap != null)
			{
				try
				{
					using var pngStream = new MemoryStream();
					bitmap.Save(pngStream); // Avalonia's Bitmap.Save defaults to PNG
					var name = $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss}.png";
					vm.AddAttachmentFromBytes(name, "image/png", pngStream.ToArray());
					e.Handled = true; // suppress the default paste so the input box doesn't get binary garbage
					return;
				}
				finally
				{
					bitmap.Dispose();
				}
			}

			// Check if clipboard contains file paths (file URIs from Finder)
#pragma warning disable CS0618 // Legacy IClipboard.GetFormatsAsync still functional for file detection.
			var formats = await clipboard.GetFormatsAsync();
			if (formats != null && formats.Contains(DataFormats.Files))
			{
				var fileObj = await clipboard.GetDataAsync(DataFormats.Files);
				if (fileObj is IEnumerable<IStorageItem> items)
				{
					var any = false;
					foreach (var item in items)
					{
						var path = item.TryGetLocalPath();
						if (!string.IsNullOrEmpty(path) && File.Exists(path))
						{
							await vm.AddAttachmentFromFileAsync(path);
							any = true;
						}
					}
					if (any) e.Handled = true;
				}
			}
#pragma warning restore CS0618
		}
		catch (Exception ex)
		{
			_log.Debug(ex, "Clipboard paste check failed (non-fatal)");
		}
	}

	/// <summary>
	/// Reads an image from the clipboard via Avalonia 11's IAsyncDataTransfer API
	/// (accessed via reflection since it isn't exposed in stable Avalonia headers).
	/// Returns the decoded Bitmap, or null if the clipboard doesn't contain an image.
	/// Caller is responsible for disposing the returned Bitmap.
	/// </summary>
	private static async Task<Avalonia.Media.Imaging.Bitmap?> TryGetClipboardBitmapAsync(
		Avalonia.Input.Platform.IClipboard clipboard)
	{
		var tryGetMethod = clipboard.GetType().GetMethod("TryGetDataAsync", System.Type.EmptyTypes);
		if (tryGetMethod == null) return null;

		var dtTask = tryGetMethod.Invoke(clipboard, null) as System.Threading.Tasks.Task;
		if (dtTask == null) return null;
		await dtTask;

		var dataTransfer = dtTask.GetType().GetProperty("Result")?.GetValue(dtTask);
		if (dataTransfer == null) return null;

		try
		{
			var itemsProp = dataTransfer.GetType().GetProperty("Items");
			if (itemsProp?.GetValue(dataTransfer) is not System.Collections.IEnumerable items)
				return null;

			foreach (var item in items)
			{
				var itemType = item.GetType();
				var fmtsProp = itemType.GetProperty("Formats");
				var tryGetRawAsync = itemType.GetMethod("TryGetRawAsync");
				if (fmtsProp?.GetValue(item) is not System.Collections.IEnumerable fmts || tryGetRawAsync == null)
					continue;

				foreach (var fmt in fmts)
				{
					try
					{
						if (tryGetRawAsync.Invoke(item, new[] { fmt }) is not System.Threading.Tasks.Task rawTask)
							continue;
						await rawTask;
						var raw = rawTask.GetType().GetProperty("Result")?.GetValue(rawTask);
						if (raw is Avalonia.Media.Imaging.Bitmap bmp)
							return bmp;
					}
					catch
					{
						// Try the next format
					}
				}
			}
		}
		finally
		{
			(dataTransfer as IDisposable)?.Dispose();
		}

		return null;
	}
}
