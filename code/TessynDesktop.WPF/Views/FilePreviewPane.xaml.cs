using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The file preview pane: header, progress, messages and the sandboxed page. The WebView2 is created the first time a preview is
/// opened, so a session that never previews a file never starts a second browser surface.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class FilePreviewPane : UserControl
{
	/// <summary>False in a preview window, whose title bar has the Close (FR.29.7); true in the side pane.</summary>
	public static readonly DependencyProperty ShowCloseButtonProperty = DependencyProperty.Register(
		nameof(ShowCloseButton), typeof(bool), typeof(FilePreviewPane), new PropertyMetadata(true, (d, e) => ((FilePreviewPane)d).CloseButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed));

	private FilePreviewViewModel? _subscribed;
	private FilePreviewSurface? _attachedSurface;
	private bool _attached;

	public FilePreviewPane()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
	}

	public bool ShowCloseButton
	{
		get => (bool)GetValue(ShowCloseButtonProperty);
		set => SetValue(ShowCloseButtonProperty, value);
	}

	/// <summary>The WebView2 of the control, null before the first preview was opened.</summary>
	public WebView2? Page => PageHost.Children.OfType<WebView2>().FirstOrDefault();

	private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		if (_subscribed != null)
			_subscribed.PropertyChanged -= OnPropertyChanged;

		_subscribed = e.NewValue as FilePreviewViewModel;
		if (_attached && !ReferenceEquals(_attachedSurface, _subscribed?.Surface))
		{
			// A new view model with its own surface (a preview window got a new renderer): the old WebView2 belongs to the old surface.
			PageHost.Children.Clear();
			_attached = false;
			_attachedSurface = null;
		}

		if (_subscribed != null)
		{
			_subscribed.PropertyChanged += OnPropertyChanged;
			if (_subscribed.IsOpen)
				EnsureAttached();
		}
	}

	private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(FilePreviewViewModel.IsOpen) && _subscribed?.IsOpen == true)
			EnsureAttached();
	}

	private void EnsureAttached()
	{
		if (_attached || _subscribed?.Surface is not FilePreviewSurface surface)
			return;

		_attached = true;
		_attachedSurface = surface;
		var control = new WebView2();
		PageHost.Children.Add(control);
		_ = AttachAsync(surface, control);
	}

	private static async Task AttachAsync(FilePreviewSurface surface, WebView2 control)
	{
		var logger = (Application.Current as App)?.Services.GetService<ILogger<FilePreviewPane>>();
		try
		{
			await surface.AttachAsync(control);
		}
		catch (Exception ex) when (surface.IsDisposed)
		{
			// The window (or pane) was closed while the control was still starting: WebView2 aborts the start. That is a close, not a failure.
			logger?.LogInformation("The preview WebView2 start was abandoned, its surface was closed: {Reason}", ex.Message);
		}
		catch (Exception ex)
		{
			logger?.LogError(ex, "The preview WebView2 failed to initialize");
		}
	}
}
