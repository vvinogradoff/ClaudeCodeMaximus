namespace TessynDesktop.WPF.Services;

/// <summary>Opens a previewed file in the system's default app, for allowlisted document and media types only.</summary>
/// <remarks>Created by Claude</remarks>
public interface IExternalFileLauncher
{
	/// <summary>Opens <paramref name="path"/> when <see cref="ExternalOpenPolicy"/> allows its type and the file is inside the preview cache; false when refused or the launch failed.</summary>
	bool TryOpen(string path);
}
