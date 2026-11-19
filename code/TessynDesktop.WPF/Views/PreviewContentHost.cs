using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The content of a preview window or a docked preview panel (FR.29.5, FR.29.18): a <see cref="FilePreviewPane"/> (header hidden, no Close) or a
/// <see cref="BrowserPreviewView"/>, whichever the <see cref="PreviewWindowViewModel"/> has. Shared by <see cref="PreviewWindow"/> and <see cref="PreviewPanelContent"/>, so a
/// window and a panel show their content the same way. A browser content gets a new view every time, so it never inherits a disposed WebView2.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class PreviewContentHost : Grid
{
	private PreviewWindowViewModel? _viewModel;
	private ILogger? _logger;
	private BrowserPreviewView? _browserView;

	public PreviewContentHost()
	{
		FilePane = new FilePreviewPane { ShowCloseButton = false, ShowHeader = false, Visibility = Visibility.Collapsed };
		BrowserHost = new Border { Visibility = Visibility.Collapsed };
		Children.Add(FilePane);
		Children.Add(BrowserHost);
	}

	/// <summary>The file renderer; collapsed while the content is not a file.</summary>
	public FilePreviewPane FilePane { get; }

	/// <summary>Holds the browser view; collapsed while the content is not a web page.</summary>
	public Border BrowserHost { get; }

	/// <summary>Shows the content of <paramref name="viewModel"/> now and whenever it changes.</summary>
	public void Attach(PreviewWindowViewModel viewModel, ILogger logger)
	{
		Detach();
		_viewModel = viewModel;
		_logger = logger;
		viewModel.PropertyChanged += OnViewModelPropertyChanged;
		ApplyContent();
	}

	/// <summary>Stops following the view model and empties the host (the view model's own disposal ends the renderer and the browser).</summary>
	public void Detach()
	{
		if (_viewModel != null)
			_viewModel.PropertyChanged -= OnViewModelPropertyChanged;

		_viewModel = null;
		FilePane.DataContext = null;
		FilePane.Visibility = Visibility.Collapsed;
		BrowserHost.Child = null;
		_browserView = null;
		BrowserHost.Visibility = Visibility.Collapsed;
	}

	private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(PreviewWindowViewModel.File) or nameof(PreviewWindowViewModel.Browser))
			ApplyContent();
	}

	/// <summary>Shows the pane of the content the view model has: a file renderer or a browser.</summary>
	private void ApplyContent()
	{
		if (_viewModel == null)
			return;

		FilePane.DataContext = _viewModel.File;
		FilePane.Visibility = _viewModel.File == null ? Visibility.Collapsed : Visibility.Visible;
		if (_viewModel.Browser == null)
		{
			BrowserHost.Child = null;
			_browserView = null;
			BrowserHost.Visibility = Visibility.Collapsed;
			return;
		}

		if (_browserView != null && ReferenceEquals(_browserView.DataContext, _viewModel.Browser))
			return;

		_browserView = new BrowserPreviewView { DataContext = _viewModel.Browser };
		BrowserHost.Child = _browserView;
		BrowserHost.Visibility = Visibility.Visible;
		if (_viewModel.BrowserContent?.Surface is BrowserSurface surface && _logger != null)
			_ = _browserView.AttachAsync(surface, _logger);
	}
}
