using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace TessynDesktop.Views;

/// <remarks>Created by Claude</remarks>
public partial class InputDialog : Window
{
	private string? _result;

	public InputDialog()
	{
		InitializeComponent();
	}

	public static async Task<string?> ShowAsync(Window owner, string title, string prompt)
	{
		var dialog = new InputDialog();
		dialog.Title           = title;
		dialog.PromptLabel.Text = prompt;
		await dialog.ShowDialog(owner);
		return dialog._result;
	}

	private void OnOk(object? sender, RoutedEventArgs e)
	{
		var text = InputBox.Text?.Trim();
		if (string.IsNullOrEmpty(text))
			return;

		_result = text;
		Close();
	}

	private void OnCancel(object? sender, RoutedEventArgs e)
		=> Close();

	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter)
			OnOk(sender, e);
	}
}
