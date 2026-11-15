using System.Windows;
using TessynDesktop.WPF.ViewModels;
using TessynDesktop.WPF.Views;

namespace TessynDesktop.WPF.Services;

/// <summary><see cref="IDialogPresenter"/> over a modal <see cref="DialogWindow"/> owned by the main window.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class WpfDialogPresenter : IDialogPresenter
{
	public async Task<DialogOutcome> PresentAsync(DialogViewModel dialog)
	{
		var window = new DialogWindow(dialog);
		var owner = Application.Current?.MainWindow;
		if (owner is { IsLoaded: true })
			window.Owner = owner;

		// ShowDialog blocks until the window closes, so the answer has to close it from outside.
		_ = dialog.Outcome.ContinueWith(_ => window.Dispatcher.BeginInvoke(() =>
		{
			if (window.IsVisible)
				window.Close();
		}), TaskScheduler.Default);
		window.ShowDialog();
		return await dialog.Outcome;
	}
}
