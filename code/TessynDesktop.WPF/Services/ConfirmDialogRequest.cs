namespace TessynDesktop.WPF.Services;

/// <summary>A yes/no question; <see cref="IsDestructive"/> makes Cancel the default button.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record ConfirmDialogRequest(string Title, string Message, string ConfirmText = Constants.DialogTexts.Confirm, string CancelText = Constants.DialogTexts.Cancel, bool IsDestructive = false);
