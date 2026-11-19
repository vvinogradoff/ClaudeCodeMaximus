namespace TessynDesktop.WPF.Services;

/// <summary>The outcome of reading a saved file's handle again at the restore.</summary>
/// <param name="Target">The read handle; null when the file cannot reopen.</param>
/// <param name="Transient">True when it failed for a reason that may be gone at the next start (not a refusal); the saved entry stays in the list.</param>
/// <remarks>Created by Claude</remarks>
public sealed record RestoredFileTarget(PreviewTarget? Target, bool Transient);
