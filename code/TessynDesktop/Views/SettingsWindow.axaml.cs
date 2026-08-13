using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TessynDesktop.Services;
using TessynDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace TessynDesktop.Views;

/// <remarks>Created by Claude</remarks>
public partial class SettingsWindow : Window
{
	public SettingsWindow()
	{
		InitializeComponent();
		KeyDown += OnWindowKeyDown;
	}

	private void OnWindowKeyDown(object? sender, KeyEventArgs e)
	{
		var keyService = App.Services.GetRequiredService<IKeyBindingService>();
		if (keyService.Matches(Constants.KeyBindings.CloseDialog, e))
		{
			e.Handled = true;
			Close();
		}
	}

	private async void OnBrowseSessionRootClicked(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not SettingsViewModel vm)
			return;

		var folders = await StorageProvider.OpenFolderPickerAsync(
			new Avalonia.Platform.Storage.FolderPickerOpenOptions
			{
				Title = "Select Session Files Directory",
				AllowMultiple = false,
			});

		if (folders.Count > 0)
			vm.SessionFilesRoot = folders[0].Path.LocalPath;
	}

	private async void OnBrowseScreensaverDirectoryClicked(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not SettingsViewModel vm)
			return;

		var folders = await StorageProvider.OpenFolderPickerAsync(
			new Avalonia.Platform.Storage.FolderPickerOpenOptions
			{
				Title = "Select Screensaver Images Directory",
				AllowMultiple = false,
			});

		if (folders.Count > 0)
			vm.ScreensaverDirectory = folders[0].Path.LocalPath;
	}

	private void OnSaveClicked(object? sender, RoutedEventArgs e)
		=> Close();

	// ── MCP Servers tab (FR.20.7) ────────────────────────────────────────────

	private async void OnAddMcpServerClicked(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not SettingsViewModel vm)
			return;

		var model = await McpServerEditDialog.ShowAsync(this, existing: null);
		if (model != null)
			vm.AddMcpServer(model);
	}

	private async void OnEditMcpServerClicked(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not SettingsViewModel vm || vm.SelectedMcpServer is not { } row)
			return;

		var model = await McpServerEditDialog.ShowAsync(this, row.Model);
		if (model != null)
			vm.ReplaceMcpServer(row, model);
	}

	private async void OnRemoveMcpServerClicked(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not SettingsViewModel vm || vm.SelectedMcpServer is not { } row)
			return;

		var confirmed = await ConfirmDialog.ShowAsync(this,
			$"Remove '{row.DisplayName}' from the MCP Server Registry?\n\nIts stored authorization (if any) will also be deleted.",
			"Yes, remove");
		if (confirmed)
			vm.RemoveMcpServer(row);
	}

	private async void OnAuthorizeMcpServerClicked(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not SettingsViewModel vm || vm.SelectedMcpServer is not { } row)
			return;

		var mcpOAuth = App.Services.GetRequiredService<IMcpOAuthService>();
		row.LastActionMessage = null;
		var result = await mcpOAuth.AuthorizeInteractiveAsync(row.Model);
		row.RefreshStatus();
		if (!result.Success)
			row.LastActionMessage = result.Message;
	}
}
