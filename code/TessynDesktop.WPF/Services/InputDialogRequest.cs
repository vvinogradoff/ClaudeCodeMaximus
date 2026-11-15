namespace TessynDesktop.WPF.Services;

/// <summary>A one-line text prompt; <see cref="Validate"/> returns an error text to keep the dialog open, or null to accept.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record InputDialogRequest(string Title, string Prompt, string InitialText = "", string ConfirmText = Constants.DialogTexts.Ok, Func<string, string?>? Validate = null);
