namespace TessynDesktop.WPF.State;

/// <summary>A settings section was written; <see cref="Section"/> is its <see cref="ClientSettingsSectionAttribute.Name"/>.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ClientSettingsChangedEventArgs : EventArgs
{
	public ClientSettingsChangedEventArgs(string section)
	{
		Section = section;
	}

	public string Section { get; }
}
