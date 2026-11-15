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
	private FilePreviewViewModel? _subscribed;
	private bool _attached;

	public FilePreviewPane()
	{
		InitializeComponent();
		DataContextChanged += OnDataContextChanged;
	}

	private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
	{
		if (_subscribed != null)
			_subscribed.PropertyChanged -= OnPropertyChanged;

		_subscribed = e.NewValue as FilePreviewViewModel;
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
		if (_attached || Application.Current is not App app)
			return;

		_attached = true;
		var control = new WebView2();
		PageHost.Children.Add(control);
		_ = AttachAsync(app, control);
	}

	private static async Task AttachAsync(App app, WebView2 control)
	{
		var logger = app.Services.GetRequiredService<ILogger<FilePreviewPane>>();
		try
		{
			await app.Services.GetRequiredService<FilePreviewSurface>().AttachAsync(control);
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "The preview WebView2 failed to initialize");
		}
	}
}
