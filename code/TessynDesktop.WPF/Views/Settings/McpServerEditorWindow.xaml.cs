using System.ComponentModel;
using System.Windows;
using TessynDesktop.WPF.ViewModels.Settings;

namespace TessynDesktop.WPF.Views.Settings;

/// <summary>
/// The modal Add/Edit window of an MCP server. It closes itself when the server was stored; Cancel and closing stop a discovery that still runs. Losing the focus of the
/// URL box starts the OAuth endpoint lookup (a no-op unless this is an OAuth server with a valid address).
/// </summary>
/// <remarks>Created by Claude</remarks>
public partial class McpServerEditorWindow : Window
{
	private readonly McpServerEditorViewModel _editor;

	public McpServerEditorWindow(McpServerEditorViewModel editor)
	{
		_editor = editor;
		DataContext = editor;
		InitializeComponent();
		editor.PropertyChanged += OnEditorChanged;
		Closed += (_, _) =>
		{
			editor.PropertyChanged -= OnEditorChanged;
			editor.CancelDiscoveryCommand.Execute(null);
		};
	}

	private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(McpServerEditorViewModel.Saved) && _editor.Saved)
			Dispatcher.BeginInvoke(() =>
			{
				if (IsVisible)
					Close();
			});
	}

	private void OnUrlLostFocus(object sender, RoutedEventArgs e) => _editor.DiscoverCommand.Execute(null);
}
