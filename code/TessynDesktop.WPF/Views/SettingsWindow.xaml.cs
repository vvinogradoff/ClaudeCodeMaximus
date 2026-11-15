using System.Windows;
using TessynDesktop.WPF.ViewModels.Settings;

namespace TessynDesktop.WPF.Views;

/// <summary>
/// The Settings window (W6a, W6b): tabs over the sections of <see cref="SettingsViewModel"/>. It reads every section when it opens and detaches the view model from the client when it closes.
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class SettingsWindow : Window
{
	private readonly SettingsViewModel _settings;

	public SettingsWindow(SettingsViewModel settings)
	{
		_settings = settings;
		DataContext = settings;
		InitializeComponent();
		Loaded += OnLoaded;
		Closed += (_, _) => _settings.Dispose();
		// IsCancel closes only a dialog; this window is modeless, so pressing Close (mouse, keyboard or UI Automation Invoke) must close it itself.
		CloseButton.Click += (_, _) => Close();
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		Loaded -= OnLoaded;
		await _settings.InitializeAsync();
	}
}
