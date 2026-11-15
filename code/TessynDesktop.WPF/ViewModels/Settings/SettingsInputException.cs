namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>A value the user typed cannot be saved; the message is shown as the section's error and nothing is sent.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class SettingsInputException : Exception
{
	public SettingsInputException(string message) : base(message)
	{
	}
}
