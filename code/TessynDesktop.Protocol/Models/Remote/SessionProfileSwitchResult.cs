using System.Collections.Generic;

namespace TessynDesktop.Models.Remote;

/// <summary>
/// Answer of the remote <c>session.switchProfile</c>: what the loopback-only <c>profile.switch</c> did, without any local path
/// (<see cref="ProfileSwitchResultDto.OldConfigPaths"/> stays on the PC; failures carry counts).
/// </summary>
/// <param name="ProfileId">The profile switched to: <c>"default"</c> or a stored profile's id.</param>
/// <param name="ProfileName">Its display name.</param>
/// <param name="NodeMoved">True when the addressed session itself was moved now; false when it was running and the switch is queued, or it was already on the profile.</param>
/// <param name="Moved">The descendants moved now (empty unless <c>includeDescendants</c>).</param>
/// <param name="Queued">Running sessions whose switch is applied when their turn ends; they are never interrupted.</param>
/// <param name="Failed">Sessions whose files could not all be moved; the switch stays queued for their next turn.</param>
/// <remarks>Created by Claude</remarks>
public sealed record SessionProfileSwitchResult(
	string ProfileId,
	string ProfileName,
	bool NodeMoved,
	IReadOnlyList<ProfileSwitchedSessionDto> Moved,
	IReadOnlyList<ProfileSwitchedSessionDto> Queued,
	IReadOnlyList<SessionProfileSwitchFailureDto> Failed);
