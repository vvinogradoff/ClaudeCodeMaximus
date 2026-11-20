namespace TessynDesktop.WPF.Services;

/// <summary>
/// The system browser launcher WITHOUT the preview routing (FR.26.9): the forced gestures (Ctrl+click, Ctrl+Shift+click in the transcript) and the explicit
/// "Open in system browser" entries use it, so a link never goes to a preview window or panel. The URL policy stays in force.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISystemUrlLauncher : IUrlLauncher
{
}
