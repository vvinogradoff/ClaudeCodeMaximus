using System.Windows;
using Microsoft.Extensions.Logging;
using TessynDesktop.Client;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Opens the Settings window (one at a time: asking again brings the open one forward). The window needs the daemon connection, so before it is up the user is told so
/// instead. The window is not modal, so a test window and the main window keep working while it is open.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class SettingsHost
{
	private readonly ConnectionViewModel _connection;
	private readonly Func<ITessynClient, SettingsViewModel> _factory;
	private readonly IDialogService _dialogs;
	private readonly ILogger<SettingsHost> _logger;
	private SettingsWindow? _window;

	public SettingsHost(ConnectionViewModel connection, Func<ITessynClient, SettingsViewModel> factory, IDialogService dialogs, ILogger<SettingsHost> logger)
	{
		_connection = connection;
		_factory = factory;
		_dialogs = dialogs;
		_logger = logger;
	}

	/// <summary>True while the window is open.</summary>
	public bool IsOpen => _window != null;

	/// <summary>True when the open Settings window has unsaved changes.</summary>
	public bool HasUnsavedChanges => _window?.HasUnsavedChanges == true;

	/// <summary>
	/// FR.4.7: asked before the main window closes. True when no Settings window is open or it has no unsaved changes, or the user chose Save or Discard (the window then
	/// closes with its owner without asking again); false when the user cancelled, which keeps both windows open. Call on the UI thread.
	/// </summary>
	public Task<bool> ConfirmCloseAsync() => _window == null ? Task.FromResult(true) : _window.ConfirmCloseAsync();

	/// <summary>Shows the window, or brings the open one to the front. Call on the UI thread.</summary>
	public async Task OpenAsync()
	{
		if (_window != null)
		{
			if (_window.WindowState == WindowState.Minimized)
				_window.WindowState = WindowState.Normal;

			_window.Activate();
			return;
		}

		var client = _connection.Client;
		if (client == null)
		{
			await _dialogs.ConfirmAsync(new ConfirmDialogRequest(Constants.Settings.WindowTitle, Constants.Settings.NotConnected, Constants.DialogTexts.Ok));
			return;
		}

		try
		{
			var window = new SettingsWindow(_factory(client));
			var owner = Application.Current?.MainWindow;
			if (owner is { IsLoaded: true })
				window.Owner = owner;

			window.Closed += (_, _) => _window = null;
			_window = window;
			window.Show();
		}
		catch (Exception ex)
		{
			_window = null;
			_logger.LogError(ex, "The Settings window could not be opened");
			throw;
		}
	}
}
