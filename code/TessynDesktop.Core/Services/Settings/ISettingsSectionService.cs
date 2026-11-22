using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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
	/// <see cref="Get"/> plus the section's live, read-only values (<see cref="ISettingsSection.AddLiveValuesAsync"/>, for example the Codex status of
	/// <c>general</c>, FR.33.17). Live values are read after the settings lock is released and never change the version. <c>settings.get</c> uses it.
	/// </summary>
	/// <exception cref="TdcpInvalidParamsException">There is no such section.</exception>
	Task<SettingsGetResult<object>> GetAsync(string section, CancellationToken cancellationToken = default) => Task.FromResult(Get(section));

	/// <summary>
	/// Applies <paramref name="patch"/> to <paramref name="section"/> when <paramref name="baseVersion"/> is the current version, saves, and returns the
	/// version after the update (the current one when the patch changed nothing; nothing is saved then).
	/// </summary>
	/// <exception cref="TdcpInvalidParamsException">No such section, or the patch is invalid or reaches outside the section.</exception>
	/// <exception cref="TdcpException">The version is stale ("Settings changed").</exception>
	long Update(string section, JsonNode patch, long baseVersion);
}
