using System.Windows;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Views;

/// <summary>The main window: banner, tree, session pane and status bar.</summary>
/// <remarks>Created by Claude</remarks>
public partial class MainWindow : Window
{
	private readonly MainViewModel _viewModel;
	private readonly CancellationTokenSource _lifetime = new();

	public MainWindow(MainViewModel viewModel)
	{
		_viewModel = viewModel;
		DataContext = viewModel;
		InitializeComponent();
		Loaded += OnLoaded;
		Closed += (_, _) => _lifetime.Cancel();
	}

	private void OnLoaded(object sender, RoutedEventArgs e)
	{
		_ = _viewModel.StartAsync(_lifetime.Token);
	}
}
