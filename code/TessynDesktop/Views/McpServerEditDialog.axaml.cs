using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TessynDesktop.Models;
using TessynDesktop.Services;
using TessynDesktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace TessynDesktop.Views;

/// <remarks>Created by Claude</remarks>
public partial class McpServerEditDialog : Window
{
	private McpServerEditViewModel? _vm;
	private CancellationTokenSource? _discoveryCts;

	public McpServerEditDialog()
	{
		InitializeComponent();
		Closed += (_, _) => _discoveryCts?.Cancel();
	}

	public static async Task<ExternalMcpServerModel?> ShowAsync(Window owner, ExternalMcpServerModel? existing)
	{
		var dialog = new McpServerEditDialog();
		var vm = new McpServerEditViewModel(existing);
		dialog._vm = vm;
		dialog.DataContext = vm;
		await dialog.ShowDialog(owner);
		return dialog.Result;
	}

	private ExternalMcpServerModel? Result { get; set; }

	private void OnSave(object? sender, RoutedEventArgs e)
	{
		if (_vm == null)
			return;

		if (!_vm.TryBuildModel(out var model))
		{
			ErrorText.Text = _vm.ErrorMessage;
			return;
		}

		Result = model;
		Close();
	}

	private void OnCancel(object? sender, RoutedEventArgs e) => Close();

	private async void OnUrlLostFocus(object? sender, RoutedEventArgs e)
	{
		if (_vm == null)
			return;

		_discoveryCts?.Cancel();
		_discoveryCts?.Dispose();
		_discoveryCts = new CancellationTokenSource();

		var mcpOAuth = App.Services.GetRequiredService<IMcpOAuthService>();
		await _vm.DiscoverEndpointsAsync(mcpOAuth, _discoveryCts.Token);
	}
}
