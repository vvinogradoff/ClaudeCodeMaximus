namespace TessynDesktop.WPF.Services;

/// <summary>How a dialog ended: <see cref="Accepted"/> false = cancelled or closed.</summary>
/// <remarks>Created by Claude</remarks>
public sealed record DialogOutcome(bool Accepted, string? Text = null, string? ChoiceId = null);
