namespace TessynDesktop.WPF.Services;

/// <summary>Opens a URL in the system browser (FR.26.3 interim: the embedded browser pane comes later).</summary>
/// <remarks>Created by Claude</remarks>
public interface IUrlLauncher
{
	/// <summary>Opens <paramref name="url"/> when <see cref="UrlPolicy"/> allows it; false when refused or the launch failed.</summary>
	bool TryOpen(string? url);
}
