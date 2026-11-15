using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>Result of the loopback-only <c>profile.switch</c> (ISSUE.013 R1).</summary>
/// <param name="ProfileId">The profile switched to: <c>"default"</c> or a stored profile's id.</param>
/// <param name="ProfileName">Its display name.</param>
/// <param name="NodeMoved">True when the addressed session itself was moved now; false when it was running and the switch is queued, or it was already on the profile.</param>
/// <param name="Moved">The descendants moved now (empty unless <c>includeDescendants</c>).</param>
/// <param name="Queued">Running sessions (descendants, and the addressed session when it is running) whose switch is applied when their turn ends; they are never interrupted.</param>
/// <param name="OldConfigPaths">Absolute paths under the old config dir found in the moved transcripts; reported, not rewritten.</param>
/// <remarks>Created by Claude</remarks>
public sealed record ProfileSwitchResultDto(
	string ProfileId,
	string ProfileName,
	bool NodeMoved,
	IReadOnlyList<ProfileSwitchedSessionDto> Moved,
	IReadOnlyList<ProfileSwitchedSessionDto> Queued,
	IReadOnlyList<string> OldConfigPaths);
