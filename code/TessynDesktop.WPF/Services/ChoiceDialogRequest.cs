namespace TessynDesktop.WPF.Services;

/// <summary>A pick among a few options, each its own button (the delay picker of a delayed send is one).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ChoiceDialogRequest(string Title, string Message, IReadOnlyList<DialogChoice> Choices, string CancelText = Constants.DialogTexts.Cancel);
