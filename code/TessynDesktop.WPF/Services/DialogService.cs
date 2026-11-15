using TessynDesktop.WPF.Infrastructure;
using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Services;

/// <summary><see cref="IDialogService"/>: builds the dialog view model, serialises requests and hands them to the presenter on the UI thread.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class DialogService : IDialogService
{
	private readonly IDialogPresenter _presenter;
	private readonly IUiDispatcher _dispatcher;
	private readonly SemaphoreSlim _one = new(1, 1);

	public DialogService(IDialogPresenter presenter, IUiDispatcher dispatcher)
	{
		_presenter = presenter;
		_dispatcher = dispatcher;
	}

	public async Task<bool> ConfirmAsync(ConfirmDialogRequest request)
	{
		var dialog = new DialogViewModel(DialogKind.Confirm, request.Title, request.Message, request.ConfirmText, request.CancelText) { IsDestructive = request.IsDestructive };
		return (await ShowAsync(dialog).ConfigureAwait(false)).Accepted;
	}

	public async Task<string?> InputAsync(InputDialogRequest request)
	{
		var dialog = new DialogViewModel(DialogKind.Input, request.Title, request.Prompt, request.ConfirmText, Constants.DialogTexts.Cancel)
		{
			InputText = request.InitialText,
			Validate = request.Validate,
		};
		var outcome = await ShowAsync(dialog).ConfigureAwait(false);
		return outcome.Accepted ? outcome.Text : null;
	}

	public async Task<string?> ChoiceAsync(ChoiceDialogRequest request)
	{
		if (request.Choices.Count == 0)
			throw new ArgumentException("A choice dialog needs at least one choice", nameof(request));

		var dialog = new DialogViewModel(DialogKind.Choice, request.Title, request.Message, string.Empty, request.CancelText) { Choices = request.Choices };
		var outcome = await ShowAsync(dialog).ConfigureAwait(false);
		return outcome.Accepted ? outcome.ChoiceId : null;
	}

	private async Task<DialogOutcome> ShowAsync(DialogViewModel dialog)
	{
		await _one.WaitAsync().ConfigureAwait(false);
		try
		{
			var done = new TaskCompletionSource<DialogOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
			_dispatcher.Post(async () =>
			{
				try
				{
					done.SetResult(await _presenter.PresentAsync(dialog));
				}
				catch (Exception ex)
				{
					done.SetException(ex);
				}
			});
			return await done.Task.ConfigureAwait(false);
		}
		finally
		{
			_one.Release();
		}
	}
}
