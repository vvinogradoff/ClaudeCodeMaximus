using System.Collections.Generic;
using System.Text.Json.Nodes;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>
/// The daemon-owned settings sections (package J3): their values, an in-memory version per section, patch updates guarded by that version, and
/// the <c>settings.changed</c> notification when a section's values change, whoever saved them (a patch, or the Avalonia Settings window).
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface ISettingsSectionService
{
	/// <summary>The sections with their current versions.</summary>
	IReadOnlyList<SettingsSectionInfoDto> GetSections();

	/// <summary>The values and version of <paramref name="section"/>.</summary>
	/// <exception cref="TdcpInvalidParamsException">There is no such section.</exception>
	SettingsGetResult<object> Get(string section);

	/// <summary>
	/// Applies <paramref name="patch"/> to <paramref name="section"/> when <paramref name="baseVersion"/> is the current version, saves, and returns the
	/// version after the update (the current one when the patch changed nothing; nothing is saved then).
	/// </summary>
	/// <exception cref="TdcpInvalidParamsException">No such section, or the patch is invalid or reaches outside the section.</exception>
	/// <exception cref="TdcpException">The version is stale ("Settings changed").</exception>
	long Update(string section, JsonNode patch, long baseVersion);
}
