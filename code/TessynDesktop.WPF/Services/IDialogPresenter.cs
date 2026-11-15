using TessynDesktop.WPF.ViewModels;

namespace TessynDesktop.WPF.Services;

/// <summary>Shows a dialog view model modally and returns when it is answered; the WPF one opens a window owned by the main window.</summary>
/// <remarks>Created by Claude</remarks>
public interface IDialogPresenter
{
	/// <summary>Called on the UI thread, never twice at the same time.</summary>
	Task<DialogOutcome> PresentAsync(DialogViewModel dialog);
}
