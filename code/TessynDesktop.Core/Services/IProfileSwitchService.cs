using System;
using System.Collections.Generic;
using TessynDesktop.Models;
using TessynDesktop.Models.Remote;

namespace TessynDesktop.Services;

/// <summary>A profile switch that cannot be done (unknown session or profile); the message is safe to show the caller.</summary>
/// <remarks>Created by Claude</remarks>
public sealed class ProfileSwitchException(string message) : Exception(message);

/// <summary>
/// ISSUE.013 R1: switching the profile of a session is a tree operation. The service moves a session (and optionally its
/// descendants) to a profile: transcript and session folder, the <see cref="SessionNodeModel.ProfileId"/> pin, and a live
/// streaming process is stopped so the next launch uses the new config dir (R7). A running session is never interrupted:
/// its switch is queued and applied when its next turn starts.
/// </summary>
/// <remarks>Created by Claude</remarks>
public interface IProfileSwitchService
{
	/// <summary>Switches the session <paramref name="nodeId"/> to the profile <paramref name="profileId"/> (<c>"default"</c> or a stored id), with its descendants when asked.</summary>
	/// <exception cref="ProfileSwitchException">The session or profile does not exist.</exception>
	ProfileSwitchResultDto Switch(string nodeId, string profileId, bool includeDescendants);

	/// <summary>Every session below <paramref name="nodeId"/> along the <see cref="SessionNodeModel.SupervisorNodeId"/> chain (not the node itself).</summary>
	IReadOnlyList<SessionNodeModel> GetDescendants(string nodeId);

	/// <summary>True when <paramref name="nodeId"/> is a descendant of <paramref name="ancestorNodeId"/>.</summary>
	bool IsDescendantOf(string nodeId, string ancestorNodeId);

	/// <summary>Applies a queued switch of this session (its turn has ended). True when one was applied.</summary>
	bool ApplyPending(SessionNodeModel node);

	/// <summary>
	/// A turn starts on the profile with this display name (the Avalonia profile change, a per-turn choice): a session that was on another
	/// profile is switched without its descendants, then the pin is recorded. Unknown names are left alone.
	/// </summary>
	void RecordTurnProfile(SessionNodeModel node, string? displayName);
}
