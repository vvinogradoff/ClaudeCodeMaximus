namespace TessynDesktop.WPF.ViewModels.Settings;

/// <summary>A section the window's Save button applies: it holds edits next to the values it loaded.</summary>
/// <remarks>Created by Claude</remarks>
public interface ISettingsEditor
{
	/// <summary>True when the editor differs from what was loaded (an invalid entry counts as a change).</summary>
	bool HasChanges { get; }

	/// <summary>Applies the edits; true when nothing was left unsaved (no change counts as saved).</summary>
	Task<bool> SaveAsync();

	/// <summary>Drops the edits and reads the stored values again.</summary>
	Task ReloadAsync();
}
