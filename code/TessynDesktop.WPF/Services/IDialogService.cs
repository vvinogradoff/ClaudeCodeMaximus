namespace TessynDesktop.WPF.Services;

/// <summary>
/// Modal questions the shell asks (confirm, input, choice), replacing the Avalonia in-window overlays. Callable from any thread;
/// requests are shown one after another, never stacked. Called from the UI thread, a dialog runs a nested message pump inside the caller's stack: do not await one while holding a lock.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IDialogService
{
	/// <summary>Answers the open dialog and every waiting request with "cancelled", and cancels later ones; called when the app exits so nothing waits forever.</summary>
	void CancelPending();

	/// <summary>True when the user confirmed.</summary>
	Task<bool> ConfirmAsync(ConfirmDialogRequest request);

	/// <summary>The accepted text, or null when cancelled.</summary>
	Task<string?> InputAsync(InputDialogRequest request);

	/// <summary>The id of the chosen <see cref="DialogChoice"/>, or null when cancelled.</summary>
	Task<string?> ChoiceAsync(ChoiceDialogRequest request);
}
