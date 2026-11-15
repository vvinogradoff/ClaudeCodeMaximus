using System.Windows;
using System.Windows.Controls;
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

	public SessionPane()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
		Loaded += OnLoaded;
		DataObject.AddPastingHandler(InputBox, OnInputPasting);
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		Loaded -= OnLoaded;
		var surface = ((App)Application.Current).Services.GetRequiredService<WebView2TranscriptSurface>();
		surface.BrowserLost += (_, _) => Dispatcher.BeginInvoke(() => _ = RecreateAsync(surface));
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
	}

	private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SessionPaneViewModel.IsSidePaneVisible))
			ApplySidePane();
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
		if (_subscribed == null || _fileDrops.Read(e.Data).Count == 0)
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
		InputBox.CaretIndex = Math.Min(caret, InputBox.Text.Length);
		InputBox.Focus();
	}
}
