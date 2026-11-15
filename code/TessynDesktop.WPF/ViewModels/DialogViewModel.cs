using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TessynDesktop.WPF.Services;

namespace TessynDesktop.WPF.ViewModels;

/// <summary>State and commands of one modal dialog; <see cref="Outcome"/> completes when the user answers. Contains no WPF types.</summary>
/// <remarks>Created by Claude</remarks>
public sealed partial class DialogViewModel : ObservableObject
{
	private readonly TaskCompletionSource<DialogOutcome> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);

	[ObservableProperty]
	private string _inputText = string.Empty;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(HasError))]
	private string? _error;

	public DialogViewModel(DialogKind kind, string title, string message, string confirmText, string cancelText)
	{
		Kind = kind;
		Title = title;
		Message = message;
		ConfirmText = confirmText;
		CancelText = cancelText;
	}

	public DialogKind Kind { get; }

	public string Title { get; }

	public string Message { get; }

	public string ConfirmText { get; }

	public string CancelText { get; }

	/// <summary>A destructive question: Cancel, not Confirm, answers on Enter.</summary>
	public bool IsDestructive { get; init; }

	public IReadOnlyList<DialogChoice> Choices { get; init; } = [];

	/// <summary>Returns an error text to keep an input dialog open, or null to accept the text.</summary>
	public Func<string, string?>? Validate { get; init; }

	public bool IsConfirm => Kind == DialogKind.Confirm;

	public bool IsInput => Kind == DialogKind.Input;

	public bool IsChoice => Kind == DialogKind.Choice;

	public bool HasError => !string.IsNullOrEmpty(Error);

	public Task<DialogOutcome> Outcome => _outcome.Task;

	[RelayCommand]
	private void Accept()
	{
		if (IsInput)
		{
			var error = Validate?.Invoke(InputText);
			if (error != null)
			{
				Error = error;
				return;
			}
		}

		_outcome.TrySetResult(new DialogOutcome(true, IsInput ? InputText : null));
	}

	[RelayCommand]
	private void Choose(DialogChoice choice) => _outcome.TrySetResult(new DialogOutcome(true, ChoiceId: choice.Id));

	[RelayCommand]
	private void Cancel() => _outcome.TrySetResult(new DialogOutcome(false));

	partial void OnInputTextChanged(string value) => Error = null;
}
