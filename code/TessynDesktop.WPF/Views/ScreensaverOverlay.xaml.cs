using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The screensaver cover (W10a): a black layer over the window with one image at a time. It follows a <see cref="ScreensaverController"/>, ticks it once a second
/// and feeds it the window's input. An image is decoded on load (<see cref="BitmapCacheOption.OnLoad"/>, so the file is not held open) at a bounded width, and
/// only the one on show is kept, so memory stays bounded however many images the folder holds.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class ScreensaverOverlay : UserControl
{
	private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(Constants.Screensaver.TickSeconds) };
	private ScreensaverController? _controller;
	private ILogger? _logger;
	private Func<DateTime> _clock = () => DateTime.UtcNow;

	public ScreensaverOverlay()
	{
		InitializeComponent();
		AutomationProperties.SetName(this, Constants.Screensaver.AutomationName);
		_timer.Tick += (_, _) => _controller?.Tick(_clock());
	}

	/// <summary>Connects the overlay to its rules and to the window's input; the clock is injectable for tests.</summary>
	public void Attach(ScreensaverController controller, ILogger? logger = null, Func<DateTime>? clock = null)
	{
		_controller = controller;
		_logger = logger;
		_clock = clock ?? _clock;
		controller.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(Refresh));

		// Every key, button and wheel input of the application counts as activity. The one that ends the screensaver is consumed: it must not also click or type into what is below.
		InputManager.Current.PreProcessInput += (_, e) =>
		{
			if (e.StagingItem.Input is not (MouseButtonEventArgs or KeyEventArgs or MouseWheelEventArgs or TouchEventArgs))
				return;

			if (_controller?.NoteInput(_clock()) == true)
				e.Cancel();
		};
		_timer.Start();
	}

	/// <summary>Shows or hides the cover and its image to match the controller.</summary>
	public void Refresh()
	{
		if (_controller is not { IsActive: true })
		{
			if (Visibility == Visibility.Visible)
				_logger?.LogInformation("Screensaver ended");

			Visibility = Visibility.Collapsed;
			Picture.Source = null;
			return;
		}

		if (Visibility != Visibility.Visible)
			_logger?.LogInformation("Screensaver started");

		Picture.Source = Load(_controller.CurrentImage);
		Visibility = Visibility.Visible;
	}

	private BitmapImage? Load(string? path)
	{
		if (path == null)
			return null;

		try
		{
			var image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.DecodePixelWidth = Constants.Screensaver.MaxDecodePixels;
			image.UriSource = new Uri(path, UriKind.Absolute);
			image.EndInit();
			image.Freeze();
			return image;
		}
		catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or FileFormatException or UriFormatException or UnauthorizedAccessException)
		{
			_logger?.LogWarning(ex, "Screensaver: image {Path} could not be loaded", path);
			return null;
		}
	}
}
