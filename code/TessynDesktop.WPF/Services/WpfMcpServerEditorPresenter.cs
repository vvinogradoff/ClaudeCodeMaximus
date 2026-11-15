using System.Windows;
using TessynDesktop.WPF.ViewModels.Settings;
using TessynDesktop.WPF.Views;
using TessynDesktop.WPF.Views.Settings;

namespace TessynDesktop.WPF.Services;

/// <summary><see cref="IMcpServerEditorPresenter"/> over a modal <see cref="McpServerEditorWindow"/> owned by the Settings window (or the main window).</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WpfMcpServerEditorPresenter : IMcpServerEditorPresenter
{
	public Task ShowAsync(McpServerEditorViewModel editor)
	{
		var window = new McpServerEditorWindow(editor);
		var owner = Application.Current?.Windows.OfType<SettingsWindow>().FirstOrDefault(w => w.IsLoaded) as Window ?? Application.Current?.MainWindow;
		if (owner is { IsLoaded: true })
			window.Owner = owner;

		window.ShowDialog();
		return Task.CompletedTask;
	}
}
