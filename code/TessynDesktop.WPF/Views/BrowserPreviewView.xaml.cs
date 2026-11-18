using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The browser bar and page of a web preview window (FR.27.4). <c>DataContext</c> is the <c>BrowserPreviewViewModel</c>. The WebView2 control
/// is created when the view is first loaded; the host gives the view the <see cref="BrowserSurface"/> to drive it with
/// <see cref="AttachAsync"/> (before or after loading: the control is created once both have happened).
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class BrowserPreviewView : UserControl
{
	private BrowserSurface? _surface;
	private ILogger? _logger;
	private bool _loaded;
	private bool _attached;

	public BrowserPreviewView()
	{
		InitializeComponent();
		Loaded += OnLoaded;
	}

	/// <summary>The WebView2 of the page; null until the view was loaded and a surface was given.</summary>
	public WebView2? Page => _attached ? PageHost.Children.OfType<WebView2>().FirstOrDefault() : null;

	/// <summary>Gives the view the surface that drives its browser. The WebView2 is created as soon as the view is loaded; a failure is logged.</summary>
	public Task AttachAsync(BrowserSurface surface, ILogger? logger = null)
	{
		_surface = surface;
		_logger = logger;
		return _loaded ? EnsureAttachedAsync() : Task.CompletedTask;
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		_loaded = true;
		if (_surface != null)
			_ = EnsureAttachedAsync();
	}

	private async Task EnsureAttachedAsync()
	{
		if (_attached || _surface == null)
			return;

		_attached = true;
		var control = new WebView2();
		PageHost.Children.Add(control);
		try
		{
			await _surface.AttachAsync(control);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "The browser WebView2 failed to initialize");
		}
	}
}
