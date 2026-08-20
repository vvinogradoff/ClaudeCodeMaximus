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
		// Yield before touching IsDiscovering (which disables the just-blurred URL field and the
		// OAuth endpoint fields via IsEnabled bindings): mutating focusable controls synchronously,
		// still on the call stack of the LostFocus routed event that's dispatching, is re-entrant
		// input-manager territory. Posting the rest of the work to the next UI-thread turn lets that
		// dispatch finish unwinding first — otherwise disabling a control while focus is still mid-move
		// (e.g. tabbing from URL into an already-configured OAuth2 entry's now-visible endpoint fields)
		// only reproduced as a hang on a *second* open of the dialog, not the first.
		await Task.Yield();

		if (_vm == null)
			return;

		_discoveryCts?.Cancel();
		_discoveryCts?.Dispose();
		_discoveryCts = new CancellationTokenSource();

		var mcpOAuth = App.Services.GetRequiredService<IMcpOAuthService>();
		await _vm.DiscoverEndpointsAsync(mcpOAuth, _discoveryCts.Token);
	}
}
