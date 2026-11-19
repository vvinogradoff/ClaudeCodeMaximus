using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;
using TessynDesktop.WPF.ViewModels;

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

	/// <summary>FR.27.10: typing in the address box pauses the page's own address updates; losing the focus ends it (the view model shows the page's address again).</summary>
	private void OnAddressGotFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (DataContext is BrowserPreviewViewModel vm)
			vm.IsEditingAddress = true;
	}

	private void OnAddressLostFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (DataContext is BrowserPreviewViewModel vm)
			vm.IsEditingAddress = false;
	}

	/// <summary>Enter opens the typed address (the page then takes the keyboard focus, as in a browser); Esc puts the page's address back.</summary>
	private void OnAddressKeyDown(object sender, KeyEventArgs e)
	{
		if (DataContext is not BrowserPreviewViewModel vm)
			return;

		if (e.Key == Key.Enter)
		{
			vm.GoToAddressCommand.Execute(null);
			if (!vm.HasError)
			{
				if (Page != null)
					Page.Focus();
				else
					Keyboard.ClearFocus();
			}

			e.Handled = true;
		}
		else if (e.Key == Key.Escape)
		{
			vm.RevertAddressCommand.Execute(null);
			e.Handled = true;
		}
	}

	/// <summary>
	/// Esc inside the page (the browser swallows it): raised again as a key event of the page's control, so the slide-out closer, which listens at the docking manager, sees it as it sees
	/// Esc anywhere else in the slide-out (FR.29 decision 4). Nothing happens when no slide-out is open.
	/// </summary>
	private void OnPageEscape(object? sender, EventArgs e)
	{
		if (Page is not { } control || PresentationSource.FromVisual(control) is not { } source)
			return;

		control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
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
			_surface.EscapePressed += OnPageEscape;
			await _surface.AttachAsync(control);
		}
		catch (Exception ex)
		{
			_logger?.LogError(ex, "The browser WebView2 failed to initialize");
		}
	}
}
