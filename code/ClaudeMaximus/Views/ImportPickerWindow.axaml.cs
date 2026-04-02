using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ClaudeMaximus.Services;
using ClaudeMaximus.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ClaudeMaximus.Views;

/// <remarks>Created by Claude</remarks>
public partial class ImportPickerWindow : Window
{
	/// <summary>
	/// The items selected for import when the user clicks "Import Selected".
	/// Null if the dialog was cancelled.
	/// </summary>
	public IReadOnlyList<ImportSessionItemViewModel>? Result { get; private set; }

	public ImportPickerWindow()
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
			OnCancelClicked(sender, e);
		}
	}

	private async void OnSearchKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key != Key.Enter)
			return;

		e.Handled = true;

		if (DataContext is ImportPickerViewModel vm)
			await vm.SearchAsync();
	}

	private async void OnSearchButtonClicked(object? sender, RoutedEventArgs e)
	{
		if (DataContext is ImportPickerViewModel vm)
			await vm.SearchAsync();
	}

	private void OnImportClicked(object? sender, RoutedEventArgs e)
	{
		if (DataContext is not ImportPickerViewModel vm)
			return;

		var selected = vm.SelectedItems;
		if (selected.Count == 0)
			return;

		Result = selected;
		Close();
	}

	private void OnCancelClicked(object? sender, RoutedEventArgs e)
	{
		if (DataContext is ImportPickerViewModel vm)
			vm.CancelTitleGeneration();

		Result = null;
		Close();
	}

	protected override void OnClosing(WindowClosingEventArgs e)
	{
		if (DataContext is ImportPickerViewModel vm)
			vm.CancelTitleGeneration();

		base.OnClosing(e);
	}
}
