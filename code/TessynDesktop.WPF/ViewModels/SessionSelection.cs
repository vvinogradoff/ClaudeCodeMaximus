namespace TessynDesktop.WPF.ViewModels;

/// <summary>The model, effort and profile ids chosen for a session (kept per session while the pane lives).</summary>
/// <remarks>Created by Claude</remarks>
public sealed record SessionSelection(string ModelId, string EffortId, string ProfileId);
