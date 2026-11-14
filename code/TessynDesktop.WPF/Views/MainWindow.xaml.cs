using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TessynDesktop.WPF.State;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The main window: banner, tree, session pane and status bar. Restores and remembers its placement and splitter.</summary>
/// <remarks>Created by Claude</remarks>
public partial class MainWindow : Window
{
	private static readonly TimeSpan PlacementSaveDelay = TimeSpan.FromMilliseconds(500);

	private readonly MainViewModel _viewModel;
	private readonly IClientStateStore _stateStore;
	private readonly Win32WindowPlacement _placement;
	private readonly WindowPlacementClamper _clamper;
	private readonly CancellationTokenSource _lifetime = new();
	private readonly DispatcherTimer _placementTimer;

	public MainWindow(MainViewModel viewModel, IClientStateStore stateStore, Win32WindowPlacement placement, WindowPlacementClamper clamper)
	{
		_viewModel = viewModel;
		_stateStore = stateStore;
		_placement = placement;
		_clamper = clamper;
		DataContext = viewModel;
		InitializeComponent();

		TreeColumn.Width = new GridLength(Math.Max(TreeColumn.MinWidth, stateStore.State.TreeWidth));
		_placementTimer = new DispatcherTimer { Interval = PlacementSaveDelay };
		_placementTimer.Tick += (_, _) =>
		{
			_placementTimer.Stop();
			CapturePlacement();
		};

		SourceInitialized += OnSourceInitialized;
		Loaded += OnLoaded;
		LocationChanged += (_, _) => RestartPlacementTimer();
		SizeChanged += (_, _) => RestartPlacementTimer();
		StateChanged += (_, _) => RestartPlacementTimer();
		Closing += (_, _) => CapturePlacement();
		Closed += (_, _) => _lifetime.Cancel();
		Splitter.DragCompleted += (_, _) => _stateStore.Update(state => state.TreeWidth = TreeColumn.ActualWidth);
	}

	private void OnSourceInitialized(object? sender, EventArgs e)
	{
		var (monitors, primary) = _placement.GetMonitors();
		var clamped = _clamper.Clamp(_stateStore.State.Window, monitors, primary);
		if (clamped != null)
			_placement.Apply(new WindowInteropHelper(this).Handle, clamped);
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		_ = _viewModel.StartAsync(_lifetime.Token);
	}

	private void RestartPlacementTimer()
	{
		_placementTimer.Stop();
		_placementTimer.Start();
	}

	private void CapturePlacement()
	{
		if (WindowState == WindowState.Minimized)
			return;

		var captured = _placement.Capture(new WindowInteropHelper(this).Handle);
		if (captured != null)
			_stateStore.Update(state => state.Window = captured);
	}
}
