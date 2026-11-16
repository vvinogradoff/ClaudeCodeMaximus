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
/// <remarks>
/// A WebView2 is a child window that always paints above WPF content, and WPF's input system never sees what goes to it. So while the cover is up the
/// <see cref="Covered"/> content is hidden (the browser windows go with it), the keyboard focus moves to the cover so a key press reaches it, and the pages report
/// their own pointer, wheel and key activity to the controller (<see cref="ScreensaverController.NotePageActivity"/>).
/// Created by Claude
/// </remarks>
public partial class ScreensaverOverlay : UserControl
{
	private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(Constants.Screensaver.TickSeconds) };
	private readonly MovementThreshold _movement = new(Constants.Screensaver.MouseMoveThresholdPixels);
	private readonly EventHandler _changed;
	private ScreensaverController? _controller;
	private ILogger? _logger;
	private Func<DateTime> _clock = () => DateTime.UtcNow;
	private bool _subscribed;
	private bool _coveredHidden;
	private IInputElement? _focusBefore;

	public ScreensaverOverlay()
	{
		InitializeComponent();
		AutomationProperties.SetName(this, Constants.Screensaver.AutomationName);
		_timer.Tick += (_, _) => _controller?.Tick(_clock());
		_changed = (_, _) => Dispatcher.BeginInvoke(new Action(Refresh));
		Loaded += (_, _) => Subscribe();
		Unloaded += (_, _) => Detach();
	}

	/// <summary>The content the cover sits on. It is hidden while the screensaver is up, which also hides the WebView2 windows that WPF cannot cover.</summary>
	public UIElement? Covered { get; set; }

	/// <summary>True while the input hook and the controller subscription are in place.</summary>
	public bool IsAttached => _subscribed;

	/// <summary>Connects the overlay to its rules and to the window's input; the clock is injectable for tests.</summary>
	public void Attach(ScreensaverController controller, ILogger? logger = null, Func<DateTime>? clock = null)
	{
		Detach();
		_controller = controller;
		_logger = logger;
		_clock = clock ?? _clock;
		Subscribe();
	}

	/// <summary>Removes the input hook (it is on the static <see cref="InputManager"/>, so it must not outlive the overlay) and the controller subscription.</summary>
	public void Detach()
	{
		if (!_subscribed)
			return;

		_subscribed = false;
		InputManager.Current.PreProcessInput -= OnPreProcessInput;
		if (_controller != null)
			_controller.Changed -= _changed;

		_timer.Stop();
	}

	private void Subscribe()
	{
		if (_subscribed || _controller == null)
			return;

		_subscribed = true;
		_controller.Changed += _changed;
		InputManager.Current.PreProcessInput += OnPreProcessInput;
		_timer.Start();
	}

	private void OnPreProcessInput(object sender, PreProcessInputEventArgs e)
	{
		// The input that ends the screensaver is consumed: it must not also click or type into what is below.
		if (HandleInput(e.StagingItem.Input))
			e.Cancel();
	}

	/// <summary>Counts one input of the application as activity. Returns true when the input ended the screensaver and must be swallowed.</summary>
	public bool HandleInput(InputEventArgs input)
	{
		switch (input)
		{
			case MouseButtonEventArgs or KeyEventArgs or MouseWheelEventArgs or TouchEventArgs:
				return _controller?.NoteInput(_clock()) == true;
			case MouseEventArgs { RoutedEvent: var routed } mouse when routed == Mouse.PreviewMouseMoveEvent:
				NoteMouseMove(PositionOf(mouse));
				return false;   // a move ends the screensaver but is not swallowed
			default:
				return false;
		}
	}

	/// <summary>A mouse position: it counts as activity only after the pointer has really travelled (see <see cref="MovementThreshold"/>).</summary>
	public void NoteMouseMove(Point position)
	{
		if (_movement.IsMovement(position))
			_controller?.NoteInput(_clock());
	}

	private Point PositionOf(MouseEventArgs mouse)
	{
		try
		{
			return mouse.GetPosition(this);
		}
		catch (InvalidOperationException)
		{
			return default;
		}
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
			ShowCovered();
			return;
		}

		if (Visibility != Visibility.Visible)
		{
			_logger?.LogInformation("Screensaver started");
			HideCovered();
		}

		Picture.Source = Load(_controller.CurrentImage);
		Visibility = Visibility.Visible;
		if (!IsKeyboardFocusWithin)
			Focus();
	}

	/// <summary>Hides the content under the cover (and with it the browser windows) and takes the keyboard focus, so a key press lands on the cover.</summary>
	private void HideCovered()
	{
		if (Covered == null || _coveredHidden)
			return;

		_focusBefore = Keyboard.FocusedElement;
		_coveredHidden = true;
		Covered.Visibility = Visibility.Hidden;
		Focusable = true;
	}

	/// <summary>Brings the content back and gives the focus to where it was.</summary>
	private void ShowCovered()
	{
		if (!_coveredHidden)
			return;

		_coveredHidden = false;
		if (Covered != null)
			Covered.Visibility = Visibility.Visible;

		Focusable = false;
		var before = _focusBefore;
		_focusBefore = null;
		if (before is UIElement { IsVisible: true } element)
			element.Focus();
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
