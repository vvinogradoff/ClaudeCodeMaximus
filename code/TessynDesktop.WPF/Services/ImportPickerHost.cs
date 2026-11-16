using System.Windows;
using Microsoft.Extensions.Logging;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Services;

/// <summary>
/// Opens the import picker (one at a time: asking again brings the open one forward). The picker needs the daemon connection and a directory or group to import into;
/// without either the user is told so. The window is not modal, so the tree and a test window keep working while it is open; after an import the first new session
/// is shown in the tree.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed class ImportPickerHost
{
	private readonly ConnectionViewModel _connection;
	private readonly TreeContext _tree;
	private readonly IDialogService _dialogs;
	private readonly ILoggerFactory _loggers;
	private ImportPickerWindow? _window;

	public ImportPickerHost(ConnectionViewModel connection, TreeContext tree, IDialogService dialogs, ILoggerFactory loggers)
	{
		_connection = connection;
		_tree = tree;
		_dialogs = dialogs;
		_loggers = loggers;
	}

	/// <summary>True while the window is open.</summary>
	public bool IsOpen => _window != null;

	/// <summary>Shows the picker for the directory or group <paramref name="near"/> (null: the selected node, else the first directory); call on the UI thread.</summary>
	public async Task OpenAsync(TreeNodeViewModel? near)
	{
		if (_window != null)
		{
			if (_window.WindowState == WindowState.Minimized)
				_window.WindowState = WindowState.Normal;

			_window.Activate();
			return;
		}

		var client = _connection.Client;
		var tree = _tree.Current;
		if (client == null || tree == null)
		{
			await _dialogs.ConfirmAsync(new ConfirmDialogRequest(Constants.Import.WindowTitle, Constants.Import.NotConnected, Constants.DialogTexts.Ok));
			return;
		}

		var targets = tree.ImportTargets();
		if (targets.Count == 0)
		{
			await _dialogs.ConfirmAsync(new ConfirmDialogRequest(Constants.Import.WindowTitle, Constants.Import.NoDirectory, Constants.DialogTexts.Ok));
			return;
		}

		var container = TreeOperations.ContainerOf(near ?? tree.SelectedNode);
		var viewModel = new ImportPickerViewModel(client, _loggers.CreateLogger<ImportPickerViewModel>());
		var logger = _loggers.CreateLogger<ImportPickerHost>();
		viewModel.Imported += async (_, nodeIds) => await ShowImportedAsync(tree, nodeIds, logger);   // async void: ShowImportedAsync catches everything
		var window = new ImportPickerWindow(viewModel);
		var owner = Application.Current?.MainWindow;
		if (owner is { IsLoaded: true })
			window.Owner = owner;

		window.Closed += (_, _) => _window = null;
		_window = window;
		window.Show();
		await viewModel.InitializeAsync(targets, container?.Id);
	}

	/// <summary>The daemon announces the new sessions with <c>tree.changed</c>; this makes sure they are there and shows the first.</summary>
	public static async Task ShowImportedAsync(TreeViewModel tree, IReadOnlyList<string> nodeIds, ILogger logger)
	{
		// This runs from an async-void event handler: an exception here (the daemon dropped right after the import) would reach the dispatcher and end the app,
		// although the import itself succeeded. The tree catches up on the next tree.changed.
		try
		{
			await tree.RefreshAsync();
			if (nodeIds.Count > 0)
				tree.RevealNode(nodeIds[0]);
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Showing the imported sessions in the tree failed");
		}
	}
}
