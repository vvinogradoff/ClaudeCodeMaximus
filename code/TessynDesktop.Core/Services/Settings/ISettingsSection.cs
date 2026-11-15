using System;
using System.Text.Json.Nodes;
using TessynDesktop.Models;

namespace TessynDesktop.Services;

/// <summary>
/// One daemon-owned settings section (package J3): the values it reads out of <see cref="AppSettingsModel"/> and the fields a patch may write.
/// Adding a section never adds a field to the model.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISettingsSection
{
	/// <summary>The section's wire name (<c>general</c>, <c>notifications</c>, <c>telegram</c>).</summary>
	string Name { get; }

	/// <summary>
	/// True for a section a client may only read (the <c>avalonia-ui</c> import): <c>settings.update</c> refuses it before anything else, and it is left
	/// out of change detection, so its version stays at the first one and no <c>settings.changed</c> is published for it.
	/// </summary>
	bool IsReadOnly => false;

	/// <summary>The section's values as its wire DTO. The caller holds the settings lock.</summary>
	object Read(AppSettingsModel settings);

	/// <summary>Captures the values <see cref="Apply"/> may write; the returned action puts them back (used when the save that follows fails).</summary>
	Action CaptureRestore(AppSettingsModel settings);

	/// <summary>
	/// Applies <paramref name="patch"/> to <paramref name="settings"/>. Every value is validated first, so a rejected patch changes nothing.
	/// A member outside the section's whitelist is rejected too. The caller holds the settings lock and saves.
	/// </summary>
	/// <exception cref="TdcpInvalidParamsException">The patch is not an object of this section, names a member outside it, or holds an invalid value.</exception>
	void Apply(AppSettingsModel settings, JsonNode patch);
}
