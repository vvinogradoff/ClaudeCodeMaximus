namespace TessynDesktop.Services;

public interface ISelfUpdateService
{
	/// <summary>
	/// True when the app is running from the project's build output directory
	/// (e.g. bin/Debug/net9.0). Self-update is suppressed in this case.
	/// </summary>
	bool IsRunningFromBuildOutput { get; }

	/// <summary>
	/// Initializes the service: auto-detects source location if needed,
	/// checks whether the app is running from build output.
	/// Must be called after settings are loaded.
	/// </summary>
	void Initialize();

	/// <summary>
	/// If a newer build exists, spawns a copy script to update the running
	/// directory after the app exits. Skipped when <see cref="IsRunningFromBuildOutput"/>
	/// is true or when source location is unknown.
	/// </summary>
	void CheckAndTriggerUpdate();
}
