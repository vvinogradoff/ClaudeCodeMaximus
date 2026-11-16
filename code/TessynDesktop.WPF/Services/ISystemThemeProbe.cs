namespace TessynDesktop.WPF.Services;

/// <summary>Tells whether Windows currently uses the light theme for apps; replaced in tests.</summary>
/// <remarks>Created by Claude</remarks>
public interface ISystemThemeProbe
{
	/// <summary>True when apps are meant to be light (the Windows default when the setting cannot be read).</summary>
	bool IsLight { get; }

	/// <summary>Windows switched its theme (raised on any thread).</summary>
	event EventHandler? Changed;
}
