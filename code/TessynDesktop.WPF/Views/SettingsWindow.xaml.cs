using System.ComponentModel;
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
	private bool _closeApproved;
	private bool _asking;

	public SettingsWindow(SettingsViewModel settings)
	{
		_settings = settings;
		DataContext = settings;
		InitializeComponent();
		Loaded += OnLoaded;
		Closed += (_, _) => _settings.Dispose();
		// IsCancel closes only a dialog; this window is modeless, so pressing Close (mouse, keyboard or UI Automation Invoke) must close it itself.
		CloseButton.Click += (_, _) => Close();
		Closing += OnClosing;
	}

	/// <summary>
	/// FR.4.7: every way of closing (the Close button, Esc, the title bar's close button, Alt+F4, the main window closing) passes here. Unsaved changes cancel the close and ask first;
	/// the window closes again once Save or Discard was chosen.
	/// </summary>
	private async void OnClosing(object? sender, CancelEventArgs e)
	{
		if (_closeApproved || !_settings.HasChanges)
			return;

		e.Cancel = true;
		if (await AskAsync())
			Dispatcher.BeginInvoke(new Action(Close));   // not inside the Closing event: a window cannot be closed while it is closing
	}

	/// <summary>True while the window holds unsaved changes that would be asked about on closing.</summary>
	public bool HasUnsavedChanges => !_closeApproved && _settings.HasChanges;

	/// <summary>Asks about the unsaved changes; true when the window may close (it then closes without asking again). One question at a time.</summary>
	public async Task<bool> ConfirmCloseAsync()
	{
		if (_closeApproved || !_settings.HasChanges)
			return true;

		return await AskAsync();
	}

	private async Task<bool> AskAsync()
	{
		if (_asking)
			return false;

		_asking = true;
		try
		{
			_closeApproved = await _settings.ConfirmCloseAsync();
			return _closeApproved;
		}
		finally
		{
			_asking = false;
		}
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		Loaded -= OnLoaded;
		await _settings.InitializeAsync();
	}
}
