using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The session pane: header, transcript (WebView2), find bar and input box.</summary>
/// <remarks>Created by Claude</remarks>
public partial class SessionPane : UserControl
{
	private SessionPaneViewModel? _subscribed;

	public SessionPane()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
		Loaded += OnLoaded;
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
			_subscribed.FindFocusRequested -= OnFindFocusRequested;

		_subscribed = e.NewValue as SessionPaneViewModel;
		if (_subscribed != null)
			_subscribed.FindFocusRequested += OnFindFocusRequested;
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
				_subscribed.FindStepCommand.Execute(-1);
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
}
