namespace TessynDesktop.WPF.ViewModels;

/// <summary>
/// What the user picked in the selectors of a session (kept per session while the pane lives). A field is null until the user picked it:
/// only picked fields are sent with a turn (tdcp-spec 9.8), a value restored from <c>lastUsed</c> is shown but never sent.
/// </summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionSelection(string? ModelId = null, string? EffortId = null, string? ProfileId = null);
