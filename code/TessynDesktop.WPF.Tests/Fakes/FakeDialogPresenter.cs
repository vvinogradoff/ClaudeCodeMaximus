using TessynDesktop.WPF.Services;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Tests.Fakes;

/// <summary>Answers each dialog with a scripted function and records the dialogs it was shown.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class FakeDialogPresenter : IDialogPresenter
{
	public List<DialogViewModel> Shown { get; } = [];

	/// <summary>Called with the dialog; it may act on the view model (set the text, execute its commands) and returns when the user "answered".</summary>
	public Func<DialogViewModel, Task> Answer { get; set; } = dialog =>
	{
		dialog.CancelCommand.Execute(null);
		return Task.CompletedTask;
	};

	public int MaxConcurrent { get; private set; }

	private int _active;

	public async Task<DialogOutcome> PresentAsync(DialogViewModel dialog)
	{
		MaxConcurrent = Math.Max(MaxConcurrent, Interlocked.Increment(ref _active));
		Shown.Add(dialog);
		try
		{
			await Answer(dialog);
			return await dialog.Outcome;
		}
		finally
		{
			Interlocked.Decrement(ref _active);
		}
	}
}
