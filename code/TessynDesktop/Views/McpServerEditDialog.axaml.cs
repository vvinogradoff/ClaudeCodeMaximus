using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TessynDesktop.Models;
using TessynDesktop.ViewModels;

namespace TessynDesktop.Views;

/// <remarks>Created by Claude</remarks>
public partial class McpServerEditDialog : Window
{
	private McpServerEditViewModel? _vm;

	public McpServerEditDialog()
	{
		InitializeComponent();
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
}
